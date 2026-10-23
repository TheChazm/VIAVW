#include "GpuBridgeProtocol.h"
#include "ModuleSnapshot.h"
#include <d3d11.h>
#include <dxgi.h>
#include <dxgi1_4.h>
#include <wincodec.h>
#include <tlhelp32.h>
#include <wrl/client.h>
#include <filesystem>
#include <iostream>
#include <sstream>
#include <iomanip>
#include <vector>
#include <chrono>
#include <thread>
using Microsoft::WRL::ComPtr;
namespace fs=std::filesystem;
static void check(HRESULT result){if(FAILED(result))throw result;}
static void win(bool ok){if(!ok)throw HRESULT_FROM_WIN32(GetLastError());}
static std::wstring wide(std::string const& value){int n=MultiByteToWideChar(CP_UTF8,0,value.data(),(int)value.size(),nullptr,0);std::wstring out(n,0);MultiByteToWideChar(CP_UTF8,0,value.data(),(int)value.size(),out.data(),n);return out;}
static std::string narrow(std::wstring const& value){int n=WideCharToMultiByte(CP_UTF8,0,value.data(),(int)value.size(),nullptr,0,nullptr,nullptr);std::string out(n,0);WideCharToMultiByte(CP_UTF8,0,value.data(),(int)value.size(),out.data(),n,nullptr,nullptr);return out;}
static std::wstring desktop(DWORD thread){wchar_t name[256]{};DWORD size=0;win(GetUserObjectInformationW(GetThreadDesktop(thread),UOI_NAME,name,sizeof(name),&size));return name;}
static fs::path modulePath(){wchar_t value[32768]{};win(GetModuleFileNameW(nullptr,value,32768)!=0);return fs::path(value);}
static unsigned moduleSnapshotRetries=0;
static uintptr_t remoteModule(DWORD pid,fs::path const& wanted){
  HANDLE snapshot=boundedModuleSnapshot(pid,moduleSnapshotRetries);MODULEENTRY32W entry{};entry.dwSize=sizeof(entry);uintptr_t result=0;
  if(Module32FirstW(snapshot,&entry))do{if(_wcsicmp(entry.szExePath,wanted.c_str())==0){result=(uintptr_t)entry.modBaseAddr;break;}}while(Module32NextW(snapshot,&entry));CloseHandle(snapshot);return result;
}
static DWORD remoteCall(HANDLE process,void* function,void* data){HANDLE thread=CreateRemoteThread(process,nullptr,0,(LPTHREAD_START_ROUTINE)function,data,0,nullptr);win(thread!=nullptr);DWORD code=0;
  DWORD wait=WaitForSingleObject(thread,3000);if(wait!=WAIT_OBJECT_0){CloseHandle(thread);throw HRESULT_FROM_WIN32(WAIT_TIMEOUT);}win(GetExitCodeThread(thread,&code));CloseHandle(thread);return code;}
struct Engine {
  char const* stage="idle";
  HANDLE mapping=nullptr;BridgeBlock* block=nullptr;DWORD pid=0;HWND root=nullptr;uint64_t created=0;uintptr_t bridgeBase=0;fs::path bridgePath;std::wstring sourceDesktop;
  ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;ComPtr<ID3D11Texture2D> texture,staging;ComPtr<IDXGIKeyedMutex> keyed;ComPtr<IWICImagingFactory> wic;uint32_t generation=0,deviceCreations=0,resourceOpens=0,stagingCreations=0;
  double waitMs=0,readbackMs=0,encodeMs=0;
  uint64_t readerLocalBytes=0,readerNonLocalBytes=0;bool readerMemorySupported=false;
  std::vector<BYTE> convertedPixels;
  std::string adapterName;bool softwareAdapter=false;
  ~Engine(){if(block)UnmapViewOfFile(block);if(mapping)CloseHandle(mapping);}
  HANDLE source(DWORD access,bool paint=false,int clientWidth=0,int clientHeight=0){
    stage="open-source";
    HANDLE process=OpenProcess(access,FALSE,pid);win(process!=nullptr);
    try{stage="source-path";wchar_t path[32768]{};DWORD size=32768;win(QueryFullProcessImageNameW(process,0,path,&size));
#ifdef VIAVW_QIV_RESEARCH
      auto expected=modulePath().parent_path().parent_path().parent_path()/L".deps"/L"qiv-3.2.0.452"/L"QuickImageViewer.exe";
      if(_wcsicmp(path,expected.c_str())!=0)throw E_ACCESSDENIED;
#elif defined(VIAVW_DYNAMIC_RESEARCH)
      auto expected=modulePath().parent_path()/L"DynamicD3DProbe.exe";auto mpv=modulePath().parent_path().parent_path().parent_path()/L".deps"/L"mpv-7c4f662c0"/L"mpv.exe";
      if(_wcsicmp(path,expected.c_str())!=0 && _wcsicmp(path,mpv.c_str())!=0)throw E_ACCESSDENIED;
#else
      auto expected=modulePath().parent_path()/L"D3DProbe.exe";if(_wcsicmp(path,expected.c_str())!=0)throw E_ACCESSDENIED;
#endif
      stage="source-creation";
      FILETIME c,e,k,u;win(GetProcessTimes(process,&c,&e,&k,&u));ULARGE_INTEGER stamp{};stamp.LowPart=c.dwLowDateTime;stamp.HighPart=c.dwHighDateTime;if(stamp.QuadPart!=created)throw E_ACCESSDENIED;
      // HWND lookup is desktop-relative. Validate on a short-lived thread attached to
      // the known owned desktop; keep the graphics reader on its original desktop.
      stage="source-window-desktop";if(sourceDesktop.rfind(L"VIAVW_",0)!=0)throw E_ACCESSDENIED;
      HRESULT validation=E_ACCESSDENIED;
      std::thread validator([&]{HDESK prior=GetThreadDesktop(GetCurrentThreadId());HDESK target=OpenDesktopW(sourceDesktop.c_str(),0,FALSE,DESKTOP_READOBJECTS|DESKTOP_WRITEOBJECTS);if(!target){validation=HRESULT_FROM_WIN32(GetLastError());return;}
        try{stage="set-validation-desktop";win(SetThreadDesktop(target));DWORD windowPid=0;GetWindowThreadProcessId(root,&windowPid);stage="source-window-pid";if(windowPid!=pid)throw E_ACCESSDENIED;stage="source-window-parent";if(GetParent(root))throw E_ACCESSDENIED;
          stage="source-desktop-membership";struct Search{HWND root;bool found;} search{root,false};win(EnumDesktopWindows(target,[](HWND window,LPARAM arg)->BOOL{auto search=(Search*)arg;if(window==search->root)search->found=true;return TRUE;},(LPARAM)&search));if(!search.found)throw E_ACCESSDENIED;
#ifdef VIAVW_DYNAMIC_RESEARCH
          if(clientWidth){stage="private-client-resize";RECT outer{},client{};win(GetWindowRect(root,&outer));win(GetClientRect(root,&client));win(SetWindowPos(root,nullptr,0,0,clientWidth+(outer.right-outer.left)-(client.right-client.left),clientHeight+(outer.bottom-outer.top)-(client.bottom-client.top),SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE));}
#endif
          if(paint){stage="private-paint-request";win(PostMessageW(root,WM_PAINT,0,0));}validation=S_OK;
        }catch(HRESULT result){validation=result;}
        SetThreadDesktop(prior);CloseDesktop(target);
      });validator.join();check(validation);
    }catch(...){CloseHandle(process);throw;}return process;
  }
  void install(DWORD sourcePid,HWND window,uint64_t stamp,std::wstring const& mappingName,std::wstring const& desktopName){
    pid=sourcePid;root=window;created=stamp;sourceDesktop=desktopName;
#ifdef VIAVW_QIV_RESEARCH
    bridgePath=modulePath().parent_path()/L"QivPresentBridge.dll";
#elif defined(VIAVW_DYNAMIC_RESEARCH)
    bridgePath=modulePath().parent_path()/L"DynamicPresentBridge.dll";
#else
    bridgePath=modulePath().parent_path()/L"PresentBridge.dll";
#endif
    mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,mappingName.c_str());win(mapping!=nullptr);block=(BridgeBlock*)MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(BridgeBlock));win(block!=nullptr);
    stage="install-protocol";if(block->magic!=0x56494156 || block->version!=1 || block->pid!=pid || block->window!=(uint64_t)(uintptr_t)root)throw E_ACCESSDENIED;
    HANDLE process=source(PROCESS_CREATE_THREAD|PROCESS_QUERY_INFORMATION|PROCESS_VM_OPERATION|PROCESS_VM_WRITE|PROCESS_VM_READ);
    void* memory=nullptr;
    try{
      stage="load-library-module";auto load=GetProcAddress(GetModuleHandleW(L"kernel32.dll"),"LoadLibraryW");HMODULE owner=nullptr;win(GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,(LPCWSTR)load,&owner));wchar_t ownerPath[32768]{};win(GetModuleFileNameW(owner,ownerPath,32768)!=0);uintptr_t remoteOwner=remoteModule(pid,ownerPath);if(!remoteOwner)throw E_ACCESSDENIED;
      std::wstring dll=bridgePath.wstring();memory=VirtualAllocEx(process,nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);win(memory!=nullptr);SIZE_T written=0;win(WriteProcessMemory(process,memory,dll.c_str(),(dll.size()+1)*sizeof(wchar_t),&written));remoteCall(process,(void*)(remoteOwner+(uintptr_t)load-(uintptr_t)owner),memory);
      bridgeBase=remoteModule(pid,bridgePath);if(!bridgeBase)throw E_NOINTERFACE;
      // DONT_RESOLVE_DLL_REFERENCES maps only for export RVAs; DllMain never runs here.
      HMODULE local=LoadLibraryExW(bridgePath.c_str(),nullptr,DONT_RESOLVE_DLL_REFERENCES);win(local!=nullptr);auto initialize=GetProcAddress(local,"BridgeInitialize");uintptr_t offset=(uintptr_t)initialize-(uintptr_t)local;FreeLibrary(local);if(!initialize)throw E_NOINTERFACE;
      stage="adapter-initialize";BridgeInit init{};if(mappingName.size()>=128)throw E_INVALIDARG;wcscpy_s(init.mapping,mappingName.c_str());init.pid=pid;init.window=(uint64_t)(uintptr_t)root;
#if defined(VIAVW_DYNAMIC_RESEARCH) && !defined(VIAVW_QIV_RESEARCH)
      wchar_t image[32768]{};DWORD imageSize=32768;win(QueryFullProcessImageNameW(process,0,image,&imageSize));init.reserved=_wcsicmp(fs::path(image).filename().c_str(),L"mpv.exe")?1:0;
#endif
      win(WriteProcessMemory(process,memory,&init,sizeof(init),&written));DWORD code=remoteCall(process,(void*)(bridgeBase+offset),memory);if(code)throw (HRESULT)code;
    }catch(HRESULT result){if(memory && result!=HRESULT_FROM_WIN32(WAIT_TIMEOUT))VirtualFreeEx(process,memory,0,MEM_RELEASE);CloseHandle(process);throw;}
    catch(...){if(memory)VirtualFreeEx(process,memory,0,MEM_RELEASE);CloseHandle(process);throw;}
    VirtualFreeEx(process,memory,0,MEM_RELEASE);CloseHandle(process);
  }
  void attach(DWORD sourcePid,HWND window,uint64_t stamp,std::wstring const& mappingName,std::wstring const& desktopName){
    pid=sourcePid;root=window;created=stamp;sourceDesktop=desktopName;
#ifdef VIAVW_QIV_RESEARCH
    bridgePath=modulePath().parent_path()/L"QivPresentBridge.dll";
#elif defined(VIAVW_DYNAMIC_RESEARCH)
    bridgePath=modulePath().parent_path()/L"DynamicPresentBridge.dll";
#else
    bridgePath=modulePath().parent_path()/L"PresentBridge.dll";
#endif
    HANDLE process=source(PROCESS_QUERY_LIMITED_INFORMATION);CloseHandle(process);
    mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,mappingName.c_str());win(mapping!=nullptr);block=(BridgeBlock*)MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(BridgeBlock));win(block!=nullptr);
    if(block->magic!=0x56494156 || block->version!=1 || block->pid!=pid || block->window!=(uint64_t)(uintptr_t)root || !block->initialized)throw E_ACCESSDENIED;
    bridgeBase=remoteModule(pid,bridgePath);if(!bridgeBase)throw E_NOINTERFACE;
  }
  void openTexture(){
    if(!device){ComPtr<IDXGIFactory1> factory;check(CreateDXGIFactory1(IID_PPV_ARGS(&factory)));ComPtr<IDXGIAdapter1> selected;
      for(UINT index=0;;index++){ComPtr<IDXGIAdapter1> adapter;if(factory->EnumAdapters1(index,&adapter)==DXGI_ERROR_NOT_FOUND)break;DXGI_ADAPTER_DESC1 desc{};check(adapter->GetDesc1(&desc));if(desc.AdapterLuid.LowPart==block->adapter.LowPart && desc.AdapterLuid.HighPart==block->adapter.HighPart){selected=adapter;adapterName=narrow(desc.Description);softwareAdapter=(desc.Flags&DXGI_ADAPTER_FLAG_SOFTWARE)!=0;break;}}
      if(!selected)throw E_NOINTERFACE;D3D_FEATURE_LEVEL level;check(D3D11CreateDevice(selected.Get(),D3D_DRIVER_TYPE_UNKNOWN,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,&level,&context));check(CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&wic)));deviceCreations++;
    }
    if(generation!=block->generation){texture.Reset();keyed.Reset();staging.Reset();check(device->OpenSharedResource((HANDLE)(uintptr_t)block->texture,IID_PPV_ARGS(&texture)));check(texture.As(&keyed));D3D11_TEXTURE2D_DESC desc{};texture->GetDesc(&desc);if(desc.Width>8192 || desc.Height>8192 || uint64_t(desc.Width)*desc.Height>20000000)throw E_INVALIDARG;desc.MiscFlags=0;desc.BindFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;check(device->CreateTexture2D(&desc,nullptr,&staging));generation=block->generation;resourceOpens++;stagingCreations++;}
  }
  double capture(fs::path const& path,bool invalidate){
    stage="capture-validation";
    if(!block || !block->initialized)throw E_UNEXPECTED;
    auto output=fs::weakly_canonical(path);auto artifacts=fs::weakly_canonical(modulePath().parent_path().parent_path());auto rel=output.lexically_relative(artifacts);if(rel.empty() || rel.wstring().rfind(L"..",0)==0 || output.extension()!=L".png")throw E_ACCESSDENIED;
    auto start=std::chrono::steady_clock::now();LONG request=InterlockedIncrement(&block->requested);HANDLE process=source(PROCESS_QUERY_LIMITED_INFORMATION,invalidate);CloseHandle(process);
    stage="wait-for-present";
    while(InterlockedCompareExchange(&block->completed,0,0)!=request){if(std::chrono::steady_clock::now()-start>std::chrono::seconds(2))throw HRESULT_FROM_WIN32(WAIT_TIMEOUT);Sleep(1);}
    waitMs=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();stage="readback";
    if(block->error)throw (HRESULT)block->error;openTexture();HRESULT acquired=keyed->AcquireSync(1,1000);if(acquired!=S_OK)throw HRESULT_FROM_WIN32(acquired);
    try{
      context->CopyResource(staging.Get(),texture.Get());D3D11_MAPPED_SUBRESOURCE mapped{};check(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped));
      auto encoded=std::chrono::steady_clock::now();readbackMs=std::chrono::duration<double,std::milli>(encoded-start).count()-waitMs;stage="encode-png";
      try{
        BYTE* pixels=(BYTE*)mapped.pData;UINT stride=mapped.RowPitch;
        // PNG/WIC negotiated a different layout for RGBA. Normalize explicitly;
        // never label a successful encode as proof that the channels are right.
        if(block->format==DXGI_FORMAT_R8G8B8A8_UNORM || block->format==DXGI_FORMAT_R8G8B8A8_UNORM_SRGB){
          stride=block->width*4;convertedPixels.resize(size_t(stride)*block->height);
          for(UINT y=0;y<block->height;y++)for(UINT x=0;x<block->width;x++){BYTE* from=(BYTE*)mapped.pData+size_t(y)*mapped.RowPitch+x*4;BYTE* to=convertedPixels.data()+size_t(y)*stride+x*4;to[0]=from[2];to[1]=from[1];to[2]=from[0];to[3]=from[3];}pixels=convertedPixels.data();
        }
        ComPtr<IWICStream> stream;check(wic->CreateStream(&stream));check(stream->InitializeFromFilename(output.c_str(),GENERIC_WRITE));ComPtr<IWICBitmapEncoder> encoder;check(wic->CreateEncoder(GUID_ContainerFormatPng,nullptr,&encoder));check(encoder->Initialize(stream.Get(),WICBitmapEncoderNoCache));ComPtr<IWICBitmapFrameEncode> frame;check(encoder->CreateNewFrame(&frame,nullptr));check(frame->Initialize(nullptr));check(frame->SetSize(block->width,block->height));WICPixelFormatGUID format=GUID_WICPixelFormat32bppBGRA;check(frame->SetPixelFormat(&format));if(!IsEqualGUID(format,GUID_WICPixelFormat32bppBGRA))throw E_NOTIMPL;check(frame->WritePixels(block->height,stride,stride*block->height,pixels));check(frame->Commit());check(encoder->Commit());
      }
      catch(...){context->Unmap(staging.Get(),0);throw;}context->Unmap(staging.Get(),0);encodeMs=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-encoded).count();
    }catch(...){keyed->ReleaseSync(0);InterlockedExchange(&block->acknowledged,request);throw;}check(keyed->ReleaseSync(0));InterlockedExchange(&block->acknowledged,request);
    ComPtr<IDXGIDevice> dxgi;ComPtr<IDXGIAdapter> adapter;ComPtr<IDXGIAdapter3> memory;DXGI_QUERY_VIDEO_MEMORY_INFO local{},nonlocal{};
    readerMemorySupported=SUCCEEDED(device.As(&dxgi)) && SUCCEEDED(dxgi->GetAdapter(&adapter)) && SUCCEEDED(adapter.As(&memory)) && SUCCEEDED(memory->QueryVideoMemoryInfo(0,DXGI_MEMORY_SEGMENT_GROUP_LOCAL,&local)) && SUCCEEDED(memory->QueryVideoMemoryInfo(0,DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL,&nonlocal));if(readerMemorySupported){readerLocalBytes=local.CurrentUsage;readerNonLocalBytes=nonlocal.CurrentUsage;}
    return std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();
  }
  DWORD callExport(char const* name){
    if(!bridgeBase)return 0;HANDLE process=source(PROCESS_CREATE_THREAD|PROCESS_QUERY_INFORMATION|PROCESS_VM_OPERATION|PROCESS_VM_WRITE|PROCESS_VM_READ);try{HMODULE local=LoadLibraryExW(bridgePath.c_str(),nullptr,DONT_RESOLVE_DLL_REFERENCES);win(local!=nullptr);auto function=GetProcAddress(local,name);uintptr_t offset=(uintptr_t)function-(uintptr_t)local;FreeLibrary(local);if(!function)throw E_NOINTERFACE;DWORD result=remoteCall(process,(void*)(bridgeBase+offset),nullptr);CloseHandle(process);texture.Reset();keyed.Reset();staging.Reset();generation=0;return result;}catch(...){CloseHandle(process);throw;}
  }
};
int main(){CoInitializeEx(nullptr,COINIT_MULTITHREADED);Engine engine;std::string line;
  while(std::getline(std::cin,line)){
    std::istringstream input(line);std::vector<std::string> args;std::string part;while(std::getline(input,part,'\t'))args.push_back(part);
    try{
      if(args.size()==6 && args[0]=="install"){engine.install(std::stoul(args[1]),(HWND)(uintptr_t)std::stoull(args[2]),std::stoull(args[3]),wide(args[4]),wide(args[5]));std::cout<<"{\"ok\":true,\"installed\":true,\"moduleSnapshotRetries\":"<<moduleSnapshotRetries<<"}"<<std::endl;}
      else if(args.size()==6 && args[0]=="attach"){engine.attach(std::stoul(args[1]),(HWND)(uintptr_t)std::stoull(args[2]),std::stoull(args[3]),wide(args[4]),wide(args[5]));std::cout<<"{\"ok\":true,\"attached\":true,\"moduleSnapshotRetries\":"<<moduleSnapshotRetries<<"}"<<std::endl;}
      else if(args.size()==2 && (args[0]=="capture" || args[0]=="capture-paint")){double ms=engine.capture(wide(args[1]),args[0]=="capture-paint");std::cout<<"{\"ok\":true,\"ms\":"<<ms<<",\"waitMs\":"<<engine.waitMs<<",\"readbackMs\":"<<engine.readbackMs<<",\"encodeMs\":"<<engine.encodeMs<<",\"sequence\":"<<engine.block->completed<<",\"copies\":"<<engine.block->copied<<",\"deviceCreations\":"<<engine.deviceCreations<<",\"resourceOpens\":"<<engine.resourceOpens<<",\"stagingCreations\":"<<engine.stagingCreations<<",\"sourceMemorySupported\":"<<(engine.block->memorySupported?"true":"false")<<",\"sourceLocalBytes\":"<<engine.block->sourceLocalBytes<<",\"sourceNonLocalBytes\":"<<engine.block->sourceNonLocalBytes<<",\"readerMemorySupported\":"<<(engine.readerMemorySupported?"true":"false")<<",\"readerLocalBytes\":"<<engine.readerLocalBytes<<",\"readerNonLocalBytes\":"<<engine.readerNonLocalBytes<<",\"width\":"<<engine.block->width<<",\"height\":"<<engine.block->height<<",\"generation\":"<<engine.block->generation<<",\"present1Calls\":"<<engine.block->present1Calls<<",\"resizeCalls\":"<<engine.block->resizeCalls<<",\"sourceDevices\":"<<engine.block->sourceDevices<<",\"adapterName\":"<<std::quoted(engine.adapterName)<<",\"softwareAdapter\":"<<(engine.softwareAdapter?"true":"false")<<"}"<<std::endl;}
      else if(args.size()==1 && (args[0]=="stop" || args[0]=="reset")){DWORD result=engine.callExport(args[0]=="stop"?"BridgeStop":"BridgeReset");std::cout<<"{\"ok\":"<<(result?"false":"true")<<",\"code\":"<<result<<"}"<<std::endl;}
#ifdef VIAVW_DYNAMIC_RESEARCH
      else if(args.size()==3 && args[0]=="resize"){int width=std::stoi(args[1]),height=std::stoi(args[2]);if(width<160 || width>1920 || height<120 || height>1080)throw E_INVALIDARG;HANDLE source=engine.source(PROCESS_QUERY_LIMITED_INFORMATION,false,width,height);CloseHandle(source);std::cout<<"{\"ok\":true}"<<std::endl;}
#endif
      else if(args.size()==1 && args[0]=="exit")break;
      else throw E_INVALIDARG;
    }catch(HRESULT result){std::cout<<"{\"ok\":false,\"hresult\":"<<(long)result<<",\"stage\":\""<<engine.stage<<"\"}"<<std::endl;}
    catch(std::exception const&){std::cout<<"{\"ok\":false,\"exception\":true}"<<std::endl;}
  }return 0;
}

