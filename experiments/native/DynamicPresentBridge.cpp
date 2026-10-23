#include "GpuBridgeProtocol.h"
#include <d3d11.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
#include <atomic>
#include <string>
using Microsoft::WRL::ComPtr;
using PresentFn=HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*,UINT,UINT);
using Present1Fn=HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain1*,UINT,UINT,const DXGI_PRESENT_PARAMETERS*);
using ResizeFn=HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*,UINT,UINT,UINT,DXGI_FORMAT,UINT);
using KeyFn=SHORT(WINAPI*)(int);
static PresentFn originalPresent=nullptr;
static Present1Fn originalPresent1=nullptr;
static ResizeFn originalResize=nullptr;
static KeyFn originalKey=nullptr;
static void **presentSlot=nullptr,**present1Slot=nullptr,**resizeSlot=nullptr,**keySlot=nullptr;
static HANDLE mapping=nullptr;
static BridgeBlock* block=nullptr;
static std::atomic<long> enabled{0},inFlight{0};
static thread_local int presenting=0;
static SRWLOCK resources=SRWLOCK_INIT;
static ComPtr<ID3D11Device> sourceDevice;
static ComPtr<ID3D11DeviceContext> sourceContext;
static ComPtr<ID3D11Texture2D> shared;
static ComPtr<IDXGIKeyedMutex> keyed;
static D3D11_TEXTURE2D_DESC sharedDesc{};
static void check(HRESULT value){if(FAILED(value))throw value;}
static void replace(void** slot,void* value){DWORD old=0;if(!VirtualProtect(slot,sizeof(void*),PAGE_READWRITE,&old))throw HRESULT_FROM_WIN32(GetLastError());InterlockedExchangePointer(slot,value);DWORD ignored;VirtualProtect(slot,sizeof(void*),old,&ignored);}
static bool isSource(IDXGISwapChain* swap){DXGI_SWAP_CHAIN_DESC desc{};return SUCCEEDED(swap->GetDesc(&desc)) && desc.OutputWindow==(HWND)(uintptr_t)block->window;}
static void copyRequested(IDXGISwapChain* swap,UINT flags){
  if(flags&DXGI_PRESENT_TEST)return;
  LONG request=InterlockedCompareExchange(&block->requested,0,0);
  if(request==InterlockedCompareExchange(&block->completed,0,0) || !isSource(swap))return;
  AcquireSRWLockExclusive(&resources);
  try{
    // A completed texture remains alive until the consumer has opened, copied,
    // and released it. Resizes may continue while that independent texture lives.
    if(block->completed!=block->acknowledged){InterlockedIncrement(&block->skipped);ReleaseSRWLockExclusive(&resources);return;}
    ComPtr<ID3D11Texture2D> back;ComPtr<ID3D11Device> actual;check(swap->GetBuffer(0,IID_PPV_ARGS(&back)));check(swap->GetDevice(IID_PPV_ARGS(&actual)));
    D3D11_TEXTURE2D_DESC desc{};back->GetDesc(&desc);
    if(!desc.Width || !desc.Height || desc.Width>8192 || desc.Height>8192 || uint64_t(desc.Width)*desc.Height>20000000 || desc.SampleDesc.Count!=1 || desc.ArraySize!=1)throw E_INVALIDARG;
    if(desc.Format!=DXGI_FORMAT_B8G8R8A8_UNORM && desc.Format!=DXGI_FORMAT_R8G8B8A8_UNORM && desc.Format!=DXGI_FORMAT_B8G8R8A8_UNORM_SRGB && desc.Format!=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB)throw E_NOTIMPL;
    bool newDevice=actual.Get()!=sourceDevice.Get();
    if(!shared || newDevice || desc.Width!=sharedDesc.Width || desc.Height!=sharedDesc.Height || desc.Format!=sharedDesc.Format){
      keyed.Reset();shared.Reset();
      if(newDevice){sourceContext.Reset();sourceDevice=actual;sourceDevice->GetImmediateContext(&sourceContext);InterlockedIncrement(&block->sourceDevices);}
      sharedDesc=desc;desc.Usage=D3D11_USAGE_DEFAULT;desc.CPUAccessFlags=0;desc.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_RENDER_TARGET;desc.MiscFlags=D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;desc.MipLevels=1;desc.ArraySize=1;
      check(sourceDevice->CreateTexture2D(&desc,nullptr,&shared));check(shared.As(&keyed));ComPtr<IDXGIResource> resource;check(shared.As(&resource));HANDLE handle=nullptr;check(resource->GetSharedHandle(&handle));
      ComPtr<IDXGIDevice> dxgi;ComPtr<IDXGIAdapter> adapter;check(sourceDevice.As(&dxgi));check(dxgi->GetAdapter(&adapter));DXGI_ADAPTER_DESC info{};check(adapter->GetDesc(&info));
      block->adapter=info.AdapterLuid;block->texture=(uint64_t)(uintptr_t)handle;block->width=desc.Width;block->height=desc.Height;block->format=desc.Format;block->generation++;
    }
    HRESULT acquired=keyed->AcquireSync(0,0);
    if(acquired==S_OK){
      sourceContext->CopyResource(shared.Get(),back.Get());sourceContext->Flush();check(keyed->ReleaseSync(1));
      ComPtr<IDXGIDevice> dxgi;ComPtr<IDXGIAdapter> adapter;ComPtr<IDXGIAdapter3> memory;DXGI_QUERY_VIDEO_MEMORY_INFO local{},nonlocal{};
      if(SUCCEEDED(sourceDevice.As(&dxgi)) && SUCCEEDED(dxgi->GetAdapter(&adapter)) && SUCCEEDED(adapter.As(&memory)) && SUCCEEDED(memory->QueryVideoMemoryInfo(0,DXGI_MEMORY_SEGMENT_GROUP_LOCAL,&local)) && SUCCEEDED(memory->QueryVideoMemoryInfo(0,DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL,&nonlocal))){block->sourceLocalBytes=local.CurrentUsage;block->sourceNonLocalBytes=nonlocal.CurrentUsage;InterlockedExchange(&block->memorySupported,1);}
      InterlockedIncrement(&block->copied);InterlockedExchange(&block->error,0);InterlockedExchange(&block->completed,request);
    }else{InterlockedIncrement(&block->skipped);InterlockedExchange(&block->error,acquired);}
  }catch(HRESULT error){InterlockedExchange(&block->error,error);InterlockedExchange(&block->completed,request);InterlockedExchange(&block->acknowledged,request);}
  ReleaseSRWLockExclusive(&resources);
}
static HRESULT STDMETHODCALLTYPE present(IDXGISwapChain* swap,UINT interval,UINT flags){
  inFlight.fetch_add(1);presenting++;
  if(presenting==1 && enabled.load() && block){InterlockedIncrement(&block->presents);copyRequested(swap,flags);}
  HRESULT result=originalPresent(swap,interval,flags);presenting--;inFlight.fetch_sub(1);return result;
}
static HRESULT STDMETHODCALLTYPE present1(IDXGISwapChain1* swap,UINT interval,UINT flags,const DXGI_PRESENT_PARAMETERS* parameters){
  inFlight.fetch_add(1);presenting++;
  if(presenting==1 && enabled.load() && block){InterlockedIncrement(&block->presents);InterlockedIncrement(&block->present1Calls);copyRequested(swap,flags);}
  HRESULT result=originalPresent1(swap,interval,flags,parameters);presenting--;inFlight.fetch_sub(1);return result;
}
static HRESULT STDMETHODCALLTYPE resize(IDXGISwapChain* swap,UINT count,UINT width,UINT height,DXGI_FORMAT format,UINT flags){
  inFlight.fetch_add(1);if(enabled.load() && block && isSource(swap))InterlockedIncrement(&block->resizeCalls);
  // No back-buffer reference is retained by this adapter between callbacks.
  HRESULT result=originalResize(swap,count,width,height,format,flags);inFlight.fetch_sub(1);return result;
}
static SHORT WINAPI keyState(int key){inFlight.fetch_add(1);SHORT result;
  if(enabled.load() && block && block->activeKeys && key>=0 && key<256)result=block->keys[key]?SHORT(0x8000):0;else result=originalKey(key);
  inFlight.fetch_sub(1);return result;
}
static void installKeys(){
  BYTE* base=(BYTE*)GetModuleHandleW(nullptr);auto dos=(IMAGE_DOS_HEADER*)base;auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);auto directory=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];if(!directory.VirtualAddress)throw E_NOINTERFACE;
  for(auto descriptor=(IMAGE_IMPORT_DESCRIPTOR*)(base+directory.VirtualAddress);descriptor->Name;descriptor++){
    if(!descriptor->OriginalFirstThunk)continue;auto names=(IMAGE_THUNK_DATA64*)(base+descriptor->OriginalFirstThunk);auto addresses=(IMAGE_THUNK_DATA64*)(base+descriptor->FirstThunk);
    for(size_t i=0;names[i].u1.AddressOfData;i++)if(!(names[i].u1.Ordinal&IMAGE_ORDINAL_FLAG64)){auto entry=(IMAGE_IMPORT_BY_NAME*)(base+names[i].u1.AddressOfData);if(strcmp((char*)entry->Name,"GetAsyncKeyState")==0){keySlot=(void**)&addresses[i].u1.Function;originalKey=(KeyFn)*keySlot;replace(keySlot,(void*)keyState);InterlockedExchange(&block->keyAdapterInstalled,1);return;}}
  }throw E_NOINTERFACE;
}
#ifdef VIAVW_QIV_RESEARCH
// These import guards stay active until this exact owned process exits. Stopping
// GPU capture must never re-enable application requests to confine the shared
// physical cursor. They report access denied; they do not emulate confinement.
static BOOL WINAPI denyClip(const RECT*){
  inFlight.fetch_add(1);if(enabled.load() && block)InterlockedIncrement(&block->cursorClipRequests);inFlight.fetch_sub(1);
  SetLastError(ERROR_ACCESS_DENIED);return FALSE;
}
static BOOL WINAPI denyWarp(int,int){
  inFlight.fetch_add(1);if(enabled.load() && block)InterlockedIncrement(&block->cursorWarpRequests);inFlight.fetch_sub(1);
  SetLastError(ERROR_ACCESS_DENIED);return FALSE;
}
static void installCursorGuard(){
  if(HMODULE guard=GetModuleHandleW(L"QivCursorGuard.dll")){
    auto verify=(DWORD(WINAPI*)(void*))GetProcAddress(guard,"CursorGuardVerify");
    if(!verify || verify((void*)(uintptr_t)block->window))throw E_ACCESSDENIED;
    InterlockedExchange(&block->cursorGuardInstalled,1);return;
  }
  BYTE* base=(BYTE*)GetModuleHandleW(nullptr);auto dos=(IMAGE_DOS_HEADER*)base;auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);auto directory=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
  if(!directory.VirtualAddress)throw E_NOINTERFACE;bool clipped=false;
  for(auto descriptor=(IMAGE_IMPORT_DESCRIPTOR*)(base+directory.VirtualAddress);descriptor->Name;descriptor++){
    if(!descriptor->OriginalFirstThunk)continue;auto names=(IMAGE_THUNK_DATA64*)(base+descriptor->OriginalFirstThunk);auto addresses=(IMAGE_THUNK_DATA64*)(base+descriptor->FirstThunk);
    for(size_t i=0;names[i].u1.AddressOfData;i++)if(!(names[i].u1.Ordinal&IMAGE_ORDINAL_FLAG64)){
      auto entry=(IMAGE_IMPORT_BY_NAME*)(base+names[i].u1.AddressOfData);
      if(strcmp((char*)entry->Name,"ClipCursor")==0){replace((void**)&addresses[i].u1.Function,(void*)denyClip);clipped=true;}
      if(strcmp((char*)entry->Name,"SetCursorPos")==0)replace((void**)&addresses[i].u1.Function,(void*)denyWarp);
    }
  }
  if(!clipped)throw E_NOINTERFACE;
  InterlockedExchange(&block->cursorGuardInstalled,1);
}
#endif
extern "C" __declspec(dllexport) DWORD WINAPI BridgeInitialize(void* value){
  if(block)return ERROR_ALREADY_INITIALIZED;
  try{
    auto init=(BridgeInit*)value;wchar_t image[32768]{},desktop[256]{};DWORD needed=0;GetModuleFileNameW(nullptr,image,32768);std::wstring path=image;auto name=path.substr(path.find_last_of(L"\\/")+1);
#ifdef VIAVW_QIV_RESEARCH
    if(_wcsicmp(name.c_str(),L"QuickImageViewer.exe") || init->pid!=GetCurrentProcessId())throw E_ACCESSDENIED;
#else
    if((_wcsicmp(name.c_str(),L"DynamicD3DProbe.exe") && _wcsicmp(name.c_str(),L"mpv.exe")) || init->pid!=GetCurrentProcessId())throw E_ACCESSDENIED;
#endif
    if(!GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()),UOI_NAME,desktop,sizeof(desktop),&needed) || std::wstring(desktop).rfind(L"VIAVW_",0)!=0)throw E_ACCESSDENIED;
    DWORD pid=0;GetWindowThreadProcessId((HWND)(uintptr_t)init->window,&pid);if(pid!=GetCurrentProcessId() || GetParent((HWND)(uintptr_t)init->window))throw E_ACCESSDENIED;
    mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,init->mapping);if(!mapping)throw HRESULT_FROM_WIN32(GetLastError());block=(BridgeBlock*)MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(BridgeBlock));
    if(!block || block->magic!=0x56494156 || block->version!=1 || block->pid!=init->pid || block->window!=init->window)throw E_ACCESSDENIED;
    block->reserved=GetWindowThreadProcessId((HWND)(uintptr_t)init->window,nullptr);
    HWND dummy=CreateWindowExW(0,L"STATIC",L"Private dynamic bridge initialization",WS_POPUP,0,0,8,8,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);if(!dummy)throw HRESULT_FROM_WIN32(GetLastError());
    ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;ComPtr<IDXGISwapChain> baseSwap;ComPtr<IDXGISwapChain1> modern;
    DXGI_SWAP_CHAIN_DESC desc{};desc.BufferDesc.Width=8;desc.BufferDesc.Height=8;desc.BufferDesc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.BufferCount=2;desc.OutputWindow=dummy;desc.Windowed=TRUE;desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;D3D_FEATURE_LEVEL level;
    HRESULT created=D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&desc,&baseSwap,&device,&level,&context);
    if(SUCCEEDED(created))created=baseSwap.As(&modern);
    if(SUCCEEDED(created)){auto table=*(void***)modern.Get();presentSlot=&table[8];present1Slot=&table[22];resizeSlot=&table[13];originalPresent=(PresentFn)*presentSlot;originalPresent1=(Present1Fn)*present1Slot;originalResize=(ResizeFn)*resizeSlot;}
    modern.Reset();baseSwap.Reset();context.Reset();device.Reset();DestroyWindow(dummy);check(created);
#ifdef VIAVW_QIV_RESEARCH
    installCursorGuard();
#else
    if(init->reserved&1)installKeys();
#endif
    enabled.store(1);replace(presentSlot,(void*)present);replace(present1Slot,(void*)present1);replace(resizeSlot,(void*)resize);InterlockedExchange(&block->initialized,1);return 0;
  }catch(HRESULT error){enabled.store(0);try{if(presentSlot)replace(presentSlot,(void*)originalPresent);if(present1Slot)replace(present1Slot,(void*)originalPresent1);if(resizeSlot)replace(resizeSlot,(void*)originalResize);if(keySlot)replace(keySlot,(void*)originalKey);}catch(...){}if(block)InterlockedExchange(&block->error,error);return error;}
}
extern "C" __declspec(dllexport) DWORD WINAPI BridgeStop(void*){
  enabled.store(0);try{if(presentSlot)replace(presentSlot,(void*)originalPresent);if(present1Slot)replace(present1Slot,(void*)originalPresent1);if(resizeSlot)replace(resizeSlot,(void*)originalResize);if(keySlot)replace(keySlot,(void*)originalKey);}catch(HRESULT error){return error;}
  for(int i=0;i<1000 && inFlight.load();i++)Sleep(1);if(inFlight.load())return WAIT_TIMEOUT;
  AcquireSRWLockExclusive(&resources);keyed.Reset();shared.Reset();sourceContext.Reset();sourceDevice.Reset();if(block){InterlockedExchange(&block->activeKeys,0);InterlockedExchange(&block->initialized,0);UnmapViewOfFile(block);block=nullptr;}if(mapping){CloseHandle(mapping);mapping=nullptr;}ReleaseSRWLockExclusive(&resources);return 0;
}
extern "C" __declspec(dllexport) DWORD WINAPI BridgeReset(void*){
  if(!block || !enabled.load())return ERROR_INVALID_STATE;
  AcquireSRWLockExclusive(&resources);keyed.Reset();shared.Reset();sourceContext.Reset();sourceDevice.Reset();block->texture=0;InterlockedExchange(&block->requested,block->completed);InterlockedExchange(&block->acknowledged,block->completed);InterlockedExchange(&block->error,0);ReleaseSRWLockExclusive(&resources);return 0;
}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,void*){if(reason==DLL_PROCESS_ATTACH)DisableThreadLibraryCalls(module);return TRUE;}
