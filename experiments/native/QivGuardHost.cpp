#include "QivCursorGuardProtocol.h"
#include "ModuleSnapshot.h"
#include <bcrypt.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <thread>
#include <vector>
namespace fs=std::filesystem;
static char const* stage="read-command";
static unsigned snapshotRetries=0;
static void win(bool value){if(!value)throw HRESULT_FROM_WIN32(GetLastError());}
struct Handle { HANDLE value;explicit Handle(HANDLE h):value(h){}~Handle(){if(value && value!=INVALID_HANDLE_VALUE)CloseHandle(value);}Handle(Handle const&)=delete; };
static fs::path self(){wchar_t p[32768]{};win(GetModuleFileNameW(nullptr,p,32768)!=0);return p;}
static std::wstring wide(std::string const& s){int n=MultiByteToWideChar(CP_UTF8,0,s.data(),(int)s.size(),nullptr,0);std::wstring out(n,0);MultiByteToWideChar(CP_UTF8,0,s.data(),(int)s.size(),out.data(),n);return out;}
static std::vector<std::pair<fs::path,uintptr_t>> modules(DWORD pid){
  Handle snapshot(boundedModuleSnapshot(pid,snapshotRetries));MODULEENTRY32W entry{};entry.dwSize=sizeof(entry);std::vector<std::pair<fs::path,uintptr_t>> list;
  win(Module32FirstW(snapshot.value,&entry));do{list.emplace_back(entry.szExePath,(uintptr_t)entry.modBaseAddr);}while(Module32NextW(snapshot.value,&entry));return list;
}
static uintptr_t module(DWORD pid,fs::path const& path){for(auto const& entry:modules(pid))if(_wcsicmp(entry.first.c_str(),path.c_str())==0)return entry.second;return 0;}
static bool graphicsLoaded(DWORD pid){for(auto const& entry:modules(pid)){auto name=entry.first.filename().wstring();if(!_wcsicmp(name.c_str(),L"d3d11.dll") || !_wcsicmp(name.c_str(),L"dxgi.dll"))return true;}return false;}
static void pinnedHash(fs::path const& path){
  std::ifstream stream(path,std::ios::binary);if(!stream)throw E_ACCESSDENIED;std::vector<unsigned char> bytes((std::istreambuf_iterator<char>(stream)),{});if(bytes.size()!=10262528)throw E_ACCESSDENIED;
  BCRYPT_ALG_HANDLE algorithm=nullptr;if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)throw E_FAIL;BYTE digest[32]{};
  auto status=BCryptHash(algorithm,nullptr,0,bytes.data(),(ULONG)bytes.size(),digest,sizeof(digest));BCryptCloseAlgorithmProvider(algorithm,0);if(status<0)throw E_FAIL;
  std::string hex;char const* alphabet="0123456789ABCDEF";for(BYTE byte:digest){hex+=alphabet[byte>>4];hex+=alphabet[byte&15];}
  if(hex!="852F10EFFE6A99C96B73EFD1720C56749C8AAA659C7057DA72F9D833AA0E5CD4")throw E_ACCESSDENIED;
}
static HANDLE source(DWORD pid,HWND root,uint64_t created,std::wstring const& desktop){
  stage="source-path-and-hash";HANDLE process=OpenProcess(PROCESS_CREATE_THREAD|PROCESS_QUERY_INFORMATION|PROCESS_VM_OPERATION|PROCESS_VM_WRITE|PROCESS_VM_READ,FALSE,pid);win(process!=nullptr);
  try{
    wchar_t path[32768]{};DWORD size=32768;win(QueryFullProcessImageNameW(process,0,path,&size));auto expected=self().parent_path().parent_path().parent_path()/L".deps"/L"qiv-3.2.0.452"/L"QuickImageViewer.exe";
    if(_wcsicmp(path,expected.c_str())!=0)throw E_ACCESSDENIED;pinnedHash(path);
    stage="source-creation-and-session";FILETIME c,e,k,u;win(GetProcessTimes(process,&c,&e,&k,&u));ULARGE_INTEGER stamp{};stamp.LowPart=c.dwLowDateTime;stamp.HighPart=c.dwHighDateTime;if(stamp.QuadPart!=created)throw E_ACCESSDENIED;
    DWORD callerSession=0,targetSession=0;win(ProcessIdToSessionId(GetCurrentProcessId(),&callerSession));win(ProcessIdToSessionId(pid,&targetSession));if(callerSession!=targetSession || desktop.rfind(L"VIAVW_",0)!=0)throw E_ACCESSDENIED;
    stage="source-private-desktop";HRESULT validated=E_ACCESSDENIED;
    std::thread validator([&]{HDESK prior=GetThreadDesktop(GetCurrentThreadId());HDESK target=OpenDesktopW(desktop.c_str(),0,FALSE,DESKTOP_READOBJECTS|DESKTOP_WRITEOBJECTS);if(!target){validated=HRESULT_FROM_WIN32(GetLastError());return;}
      try{win(SetThreadDesktop(target));DWORD windowPid=0;GetWindowThreadProcessId(root,&windowPid);wchar_t name[256]{};GetClassNameW(root,name,256);if(windowPid!=pid || GetParent(root) || std::wstring(name)!=L"QuickImageViewer_"+desktop)throw E_ACCESSDENIED;
        struct Search{HWND root;bool found;} search{root,false};win(EnumDesktopWindows(target,[](HWND window,LPARAM p)->BOOL{auto s=(Search*)p;if(window==s->root)s->found=true;return TRUE;},(LPARAM)&search));if(!search.found)throw E_ACCESSDENIED;validated=S_OK;
      }catch(HRESULT error){validated=error;}SetThreadDesktop(prior);CloseDesktop(target);
    });validator.join();if(FAILED(validated))throw validated;
  }catch(...){CloseHandle(process);throw;}return process;
}
static DWORD call(HANDLE process,uintptr_t function,void* argument){
  Handle thread(CreateRemoteThread(process,nullptr,0,(LPTHREAD_START_ROUTINE)function,argument,0,nullptr));win(thread.value!=nullptr);
  if(WaitForSingleObject(thread.value,3000)!=WAIT_OBJECT_0)throw HRESULT_FROM_WIN32(WAIT_TIMEOUT);DWORD code=0;win(GetExitCodeThread(thread.value,&code));return code;
}
int main(){
  std::string line;if(!std::getline(std::cin,line))return 2;auto start=std::chrono::steady_clock::now();
  try{
    std::istringstream input(line);std::vector<std::string> parts;std::string part;while(std::getline(input,part,'\t'))parts.push_back(part);
    if(parts.size()!=6 || (parts[0]!="install" && parts[0]!="verify" && parts[0]!="probe"))throw E_INVALIDARG;
    DWORD pid=std::stoul(parts[1]);HWND root=(HWND)(uintptr_t)std::stoull(parts[2]);uint64_t created=std::stoull(parts[3]);auto desktop=wide(parts[4]),mapping=wide(parts[5]);
    if(mapping.size()>=128 || mapping.rfind(L"Local\\VIAVW_QIV_GUARD_",0)!=0)throw E_INVALIDARG;
    Handle process(source(pid,root,created,desktop));auto dll=self().parent_path()/L"QivCursorGuard.dll";auto gpuDll=self().parent_path()/L"QivPresentBridge.dll";
    stage="guard-module";uintptr_t base=module(pid,dll);void* memory=nullptr;
    try{
      if(parts[0]=="install"){
        if(base)throw HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED);
        if(module(pid,gpuDll))throw HRESULT_FROM_WIN32(ERROR_INVALID_STATE);
        auto load=GetProcAddress(GetModuleHandleW(L"kernel32.dll"),"LoadLibraryW");HMODULE owner=nullptr;win(GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,(LPCWSTR)load,&owner));wchar_t ownerPath[32768]{};win(GetModuleFileNameW(owner,ownerPath,32768)!=0);uintptr_t ownerBase=module(pid,ownerPath);if(!ownerBase)throw E_ACCESSDENIED;
        auto path=dll.wstring();if((path.size()+1)*sizeof(wchar_t)>4096)throw E_INVALIDARG;memory=VirtualAllocEx(process.value,nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);win(memory!=nullptr);SIZE_T written=0;win(WriteProcessMemory(process.value,memory,path.c_str(),(path.size()+1)*sizeof(wchar_t),&written));call(process.value,ownerBase+(uintptr_t)load-(uintptr_t)owner,memory);base=module(pid,dll);
      }
      if(!base)throw E_NOINTERFACE;
      stage="guard-export";HMODULE local=LoadLibraryExW(dll.c_str(),nullptr,DONT_RESOLVE_DLL_REFERENCES);win(local!=nullptr);char const* name=parts[0]=="install"?"CursorGuardInstall":parts[0]=="verify"?"CursorGuardVerify":"CursorGuardProbe";auto function=GetProcAddress(local,name);uintptr_t offset=(uintptr_t)function-(uintptr_t)local;FreeLibrary(local);if(!function)throw E_NOINTERFACE;
      void* argument=root;
      if(parts[0]=="install"){QivGuardInit init{};wcscpy_s(init.mapping,mapping.c_str());init.pid=pid;init.window=(uint64_t)(uintptr_t)root;init.created=created;SIZE_T written=0;win(WriteProcessMemory(process.value,memory,&init,sizeof(init),&written));argument=memory;}
      DWORD code=call(process.value,base+offset,argument);if(code)throw (HRESULT)code;
    }catch(HRESULT error){if(memory && error!=HRESULT_FROM_WIN32(WAIT_TIMEOUT))VirtualFreeEx(process.value,memory,0,MEM_RELEASE);throw;}
    if(memory)VirtualFreeEx(process.value,memory,0,MEM_RELEASE);
    std::cout<<"{\"ok\":true,\"ms\":"<<std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count()<<",\"moduleSnapshotRetries\":"<<snapshotRetries<<",\"helperGraphicsModules\":"<<(graphicsLoaded(GetCurrentProcessId())?"true":"false")<<",\"gpuBridgeLoaded\":"<<(module(pid,gpuDll)?"true":"false")<<"}"<<std::endl;return 0;
  }catch(HRESULT error){std::cout<<"{\"ok\":false,\"hresult\":"<<(long)error<<",\"stage\":\""<<stage<<"\"}"<<std::endl;return 1;}
  catch(std::exception const&){std::cout<<"{\"ok\":false,\"stage\":\"invalid-command\"}"<<std::endl;return 1;}
}
