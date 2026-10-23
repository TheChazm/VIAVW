#include "GpuBridgeProtocol.h"
#include <d3d11.h>
#include <dxgi.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
#include <atomic>
#include <string>
#include <cwctype>
using Microsoft::WRL::ComPtr;
using PresentFn=HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*,UINT,UINT);
using KeyFn=SHORT(WINAPI*)(int);
static PresentFn originalPresent=nullptr;
static KeyFn originalKey=nullptr;
static void** presentSlot=nullptr;
static void** keySlot=nullptr;
static HANDLE mapping=nullptr;
static BridgeBlock* block=nullptr;
static std::atomic<long> enabled{0},inFlight{0};
static SRWLOCK resourceLock=SRWLOCK_INIT;
static ComPtr<ID3D11Device> sourceDevice;
static ComPtr<ID3D11DeviceContext> sourceContext;
static ComPtr<ID3D11Texture2D> shared;
static ComPtr<IDXGIKeyedMutex> keyed;

static void check(HRESULT result){if(FAILED(result))throw result;}
static void replace(void** slot,void* value){DWORD old=0;if(!VirtualProtect(slot,sizeof(void*),PAGE_READWRITE,&old))throw HRESULT_FROM_WIN32(GetLastError());InterlockedExchangePointer(slot,value);DWORD ignored;VirtualProtect(slot,sizeof(void*),old,&ignored);}
static std::wstring desktopName(){wchar_t name[256]{};DWORD size=0;if(!GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()),UOI_NAME,name,sizeof(name),&size))throw HRESULT_FROM_WIN32(GetLastError());return name;}
static SHORT WINAPI virtualKey(int key){
  inFlight.fetch_add(1);SHORT value;
  if(enabled.load() && block && InterlockedCompareExchange(&block->activeKeys,0,0) && key>=0 && key<256)value=block->keys[key]?SHORT(0x8000):0;
  else value=originalKey(key);
  inFlight.fetch_sub(1);return value;
}
static HRESULT STDMETHODCALLTYPE interceptPresent(IDXGISwapChain* swap,UINT interval,UINT flags){
  inFlight.fetch_add(1);
  if(enabled.load() && block){
    InterlockedIncrement(&block->presents);
    LONG request=InterlockedCompareExchange(&block->requested,0,0);
    if(request!=InterlockedCompareExchange(&block->completed,0,0) && !(flags&DXGI_PRESENT_TEST)){
      AcquireSRWLockExclusive(&resourceLock);
      try {
        DXGI_SWAP_CHAIN_DESC desc{};check(swap->GetDesc(&desc));
        if(desc.OutputWindow!=(HWND)(uintptr_t)block->window)throw E_ACCESSDENIED;
        ComPtr<ID3D11Texture2D> back;check(swap->GetBuffer(0,IID_PPV_ARGS(&back)));D3D11_TEXTURE2D_DESC texture{};back->GetDesc(&texture);
        if(texture.SampleDesc.Count!=1 || texture.Width>8192 || texture.Height>8192 || uint64_t(texture.Width)*texture.Height>20000000)throw E_INVALIDARG;
        if(!shared){
          check(swap->GetDevice(IID_PPV_ARGS(&sourceDevice)));sourceDevice->GetImmediateContext(&sourceContext);
          texture.Usage=D3D11_USAGE_DEFAULT;texture.CPUAccessFlags=0;texture.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_RENDER_TARGET;texture.MiscFlags=D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;texture.MipLevels=1;texture.ArraySize=1;
          check(sourceDevice->CreateTexture2D(&texture,nullptr,&shared));check(shared.As(&keyed));
          ComPtr<IDXGIResource> resource;check(shared.As(&resource));HANDLE handle=nullptr;check(resource->GetSharedHandle(&handle));
          ComPtr<IDXGIDevice> dxgi;check(sourceDevice.As(&dxgi));ComPtr<IDXGIAdapter> adapter;check(dxgi->GetAdapter(&adapter));DXGI_ADAPTER_DESC info{};check(adapter->GetDesc(&info));
          block->adapter=info.AdapterLuid;block->texture=(uint64_t)(uintptr_t)handle;block->width=texture.Width;block->height=texture.Height;block->format=texture.Format;block->generation++;
        }
        HRESULT acquired=keyed->AcquireSync(0,0);
        if(acquired==S_OK){sourceContext->CopyResource(shared.Get(),back.Get());sourceContext->Flush();check(keyed->ReleaseSync(1));
          ComPtr<IDXGIDevice> dxgi;ComPtr<IDXGIAdapter> adapter;ComPtr<IDXGIAdapter3> memory;DXGI_QUERY_VIDEO_MEMORY_INFO local{},nonlocal{};
          if(SUCCEEDED(sourceDevice.As(&dxgi)) && SUCCEEDED(dxgi->GetAdapter(&adapter)) && SUCCEEDED(adapter.As(&memory)) && SUCCEEDED(memory->QueryVideoMemoryInfo(0,DXGI_MEMORY_SEGMENT_GROUP_LOCAL,&local)) && SUCCEEDED(memory->QueryVideoMemoryInfo(0,DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL,&nonlocal))){block->sourceLocalBytes=local.CurrentUsage;block->sourceNonLocalBytes=nonlocal.CurrentUsage;InterlockedExchange(&block->memorySupported,1);}
          InterlockedIncrement(&block->copied);InterlockedExchange(&block->error,0);InterlockedExchange(&block->completed,request);}
        else {InterlockedIncrement(&block->skipped);InterlockedExchange(&block->error,acquired);}
      } catch(HRESULT result){InterlockedExchange(&block->error,result);InterlockedExchange(&block->completed,request);}
      ReleaseSRWLockExclusive(&resourceLock);
    }
  }
  HRESULT result=originalPresent(swap,interval,flags);inFlight.fetch_sub(1);return result;
}
static void installKeyImport(){
  auto base=(BYTE*)GetModuleHandleW(nullptr);auto dos=(IMAGE_DOS_HEADER*)base;auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);
  auto directory=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];if(!directory.VirtualAddress)throw E_NOINTERFACE;
  for(auto descriptor=(IMAGE_IMPORT_DESCRIPTOR*)(base+directory.VirtualAddress);descriptor->Name;descriptor++){
    if(!descriptor->OriginalFirstThunk)continue;
    auto names=(IMAGE_THUNK_DATA64*)(base+descriptor->OriginalFirstThunk);auto addresses=(IMAGE_THUNK_DATA64*)(base+descriptor->FirstThunk);
    for(size_t i=0;names[i].u1.AddressOfData;i++)if(!(names[i].u1.Ordinal&IMAGE_ORDINAL_FLAG64)){
      auto name=(IMAGE_IMPORT_BY_NAME*)(base+names[i].u1.AddressOfData);
      if(strcmp((char*)name->Name,"GetAsyncKeyState")==0){keySlot=(void**)&addresses[i].u1.Function;originalKey=(KeyFn)*keySlot;replace(keySlot,(void*)virtualKey);return;}
    }
  }
  throw E_NOINTERFACE;
}
extern "C" __declspec(dllexport) DWORD WINAPI BridgeInitialize(void* parameter){
  if(block)return ERROR_ALREADY_INITIALIZED;
  try {
    auto data=(BridgeInit*)parameter;wchar_t image[32768]{};GetModuleFileNameW(nullptr,image,32768);std::wstring path=image;auto name=path.substr(path.find_last_of(L"\\/")+1);
    if(_wcsicmp(name.c_str(),L"D3DProbe.exe") || data->pid!=GetCurrentProcessId() || desktopName().rfind(L"VIAVW_",0)!=0)throw E_ACCESSDENIED;
    DWORD pid=0;GetWindowThreadProcessId((HWND)(uintptr_t)data->window,&pid);if(pid!=GetCurrentProcessId() || GetParent((HWND)(uintptr_t)data->window))throw E_ACCESSDENIED;
    mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,data->mapping);if(!mapping)throw HRESULT_FROM_WIN32(GetLastError());block=(BridgeBlock*)MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(BridgeBlock));
    if(!block || block->magic!=0x56494156 || block->version!=1 || block->pid!=data->pid || block->window!=data->window)throw E_ACCESSDENIED;
    block->reserved=GetWindowThreadProcessId((HWND)(uintptr_t)data->window,nullptr);
    HWND dummy=CreateWindowExW(0,L"STATIC",L"VIAVW hook initialization",WS_POPUP,0,0,8,8,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);if(!dummy)throw HRESULT_FROM_WIN32(GetLastError());
    ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;ComPtr<IDXGISwapChain> swap;
    DXGI_SWAP_CHAIN_DESC desc{};desc.BufferDesc.Width=8;desc.BufferDesc.Height=8;desc.BufferDesc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.BufferCount=2;desc.OutputWindow=dummy;desc.Windowed=TRUE;desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;D3D_FEATURE_LEVEL level;
    HRESULT created=D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&desc,&swap,&device,&level,&context);
    if(SUCCEEDED(created)){presentSlot=&(*(void***)swap.Get())[8];originalPresent=(PresentFn)*presentSlot;}
    swap.Reset();context.Reset();device.Reset();DestroyWindow(dummy);check(created);
    installKeyImport();enabled.store(1);replace(presentSlot,(void*)interceptPresent);InterlockedExchange(&block->initialized,1);return 0;
  }catch(HRESULT result){enabled.store(0);if(keySlot)try{replace(keySlot,(void*)originalKey);}catch(...){}if(block)InterlockedExchange(&block->error,result);return (DWORD)result;}
}
extern "C" __declspec(dllexport) DWORD WINAPI BridgeStop(void*){
  enabled.store(0);try{if(presentSlot)replace(presentSlot,(void*)originalPresent);if(keySlot)replace(keySlot,(void*)originalKey);}catch(HRESULT result){return result;}
  for(int i=0;i<1000 && inFlight.load();i++)Sleep(1);if(inFlight.load())return WAIT_TIMEOUT;
  AcquireSRWLockExclusive(&resourceLock);keyed.Reset();shared.Reset();sourceContext.Reset();sourceDevice.Reset();if(block){InterlockedExchange(&block->activeKeys,0);InterlockedExchange(&block->initialized,0);UnmapViewOfFile(block);block=nullptr;}if(mapping){CloseHandle(mapping);mapping=nullptr;}ReleaseSRWLockExclusive(&resourceLock);return 0;
}
extern "C" __declspec(dllexport) DWORD WINAPI BridgeReset(void*){
  if(!block || !enabled.load())return ERROR_INVALID_STATE;
  AcquireSRWLockExclusive(&resourceLock);keyed.Reset();shared.Reset();sourceContext.Reset();sourceDevice.Reset();block->texture=0;InterlockedExchange(&block->requested,block->completed);InterlockedExchange(&block->error,0);ReleaseSRWLockExclusive(&resourceLock);return 0;
}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,void*){if(reason==DLL_PROCESS_ATTACH)DisableThreadLibraryCalls(module);return TRUE;}
