#include "QivCursorGuardProtocol.h"
#include <string>
#include <vector>
static QivGuardBlock* state=nullptr;
static HANDLE mapping=nullptr;
struct Slot { void** address; void* guard; };
static std::vector<Slot> slots;
static BOOL WINAPI denyClip(const RECT*){if(state)InterlockedIncrement(&state->clipRequests);SetLastError(ERROR_ACCESS_DENIED);return FALSE;}
static BOOL WINAPI denyWarp(int,int){if(state)InterlockedIncrement(&state->warpRequests);SetLastError(ERROR_ACCESS_DENIED);return FALSE;}
static void win(bool value){if(!value)throw HRESULT_FROM_WIN32(GetLastError());}
extern "C" __declspec(dllexport) DWORD WINAPI CursorGuardVerify(void* root){
  if(!state || !state->installed || (root && state->window!=(uint64_t)(uintptr_t)root))return ERROR_INVALID_STATE;
  // IAT pages are read-only again after installation. A compare/exchange would
  // require write access even when only used as an atomic-looking read.
  for(auto const& slot:slots)if(*(void* volatile*)slot.address!=slot.guard)return ERROR_ACCESS_DENIED;
  return 0;
}
extern "C" __declspec(dllexport) DWORD WINAPI CursorGuardInstall(void* argument){
  if(state)return ERROR_ALREADY_INITIALIZED;
  try{
    auto init=(QivGuardInit*)argument;wchar_t image[32768]{},desktop[256]{},className[256]{};DWORD size=0,pid=0;
    win(GetModuleFileNameW(nullptr,image,32768)!=0);std::wstring path=image;auto name=path.substr(path.find_last_of(L"\\/")+1);
    if(_wcsicmp(name.c_str(),L"QuickImageViewer.exe") || init->pid!=GetCurrentProcessId())throw E_ACCESSDENIED;
    win(GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()),UOI_NAME,desktop,sizeof(desktop),&size));
    if(std::wstring(desktop).rfind(L"VIAVW_",0)!=0 || GetParent((HWND)(uintptr_t)init->window))throw E_ACCESSDENIED;
    GetWindowThreadProcessId((HWND)(uintptr_t)init->window,&pid);GetClassNameW((HWND)(uintptr_t)init->window,className,256);
    if(pid!=init->pid || std::wstring(className)!=L"QuickImageViewer_"+std::wstring(desktop))throw E_ACCESSDENIED;
    FILETIME c,e,k,u;win(GetProcessTimes(GetCurrentProcess(),&c,&e,&k,&u));ULARGE_INTEGER stamp{};stamp.LowPart=c.dwLowDateTime;stamp.HighPart=c.dwHighDateTime;if(stamp.QuadPart!=init->created)throw E_ACCESSDENIED;
    if(std::wstring(init->mapping).rfind(L"Local\\VIAVW_QIV_GUARD_",0)!=0)throw E_ACCESSDENIED;
    mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,init->mapping);win(mapping!=nullptr);state=(QivGuardBlock*)MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(QivGuardBlock));win(state!=nullptr);
    if(state->magic!=0x51495647 || state->version!=1 || state->pid!=pid || state->window!=init->window || state->created!=init->created)throw E_ACCESSDENIED;
    BYTE* base=(BYTE*)GetModuleHandleW(nullptr);auto dos=(IMAGE_DOS_HEADER*)base;auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);auto directory=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];if(!directory.VirtualAddress)throw E_NOINTERFACE;
    for(auto descriptor=(IMAGE_IMPORT_DESCRIPTOR*)(base+directory.VirtualAddress);descriptor->Name;descriptor++){
      if(!descriptor->OriginalFirstThunk)continue;auto names=(IMAGE_THUNK_DATA64*)(base+descriptor->OriginalFirstThunk);auto addresses=(IMAGE_THUNK_DATA64*)(base+descriptor->FirstThunk);
      for(size_t i=0;names[i].u1.AddressOfData;i++)if(!(names[i].u1.Ordinal&IMAGE_ORDINAL_FLAG64)){
        auto entry=(IMAGE_IMPORT_BY_NAME*)(base+names[i].u1.AddressOfData);
        if(strcmp((char*)entry->Name,"ClipCursor")==0){slots.push_back({(void**)&addresses[i].u1.Function,(void*)denyClip});state->clipSlots++;}
        if(strcmp((char*)entry->Name,"SetCursorPos")==0){slots.push_back({(void**)&addresses[i].u1.Function,(void*)denyWarp});state->warpSlots++;}
      }
    }
    if(!state->clipSlots)throw E_NOINTERFACE;
    for(auto const& slot:slots){DWORD old=0;win(VirtualProtect(slot.address,sizeof(void*),PAGE_READWRITE,&old));InterlockedExchangePointer(slot.address,slot.guard);DWORD ignored;win(VirtualProtect(slot.address,sizeof(void*),old,&ignored));}
    InterlockedExchange(&state->installed,1);return CursorGuardVerify((void*)(uintptr_t)init->window);
  }catch(HRESULT error){if(state)InterlockedExchange(&state->error,error);return error;}
}
// Calls only the already verified replacement functions, never the original USER APIs.
extern "C" __declspec(dllexport) DWORD WINAPI CursorGuardProbe(void* root){
  DWORD result=CursorGuardVerify(root);if(result)return result;
  for(auto const& slot:slots){BOOL allowed=slot.guard==(void*)denyClip?denyClip(nullptr):denyWarp(1,1);if(allowed || GetLastError()!=ERROR_ACCESS_DENIED)return ERROR_INVALID_FUNCTION;}return 0;
}
// The guard and its tiny mapping remain resident until this exact owned source exits.
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,void*){if(reason==DLL_PROCESS_ATTACH)DisableThreadLibraryCalls(module);return TRUE;}
