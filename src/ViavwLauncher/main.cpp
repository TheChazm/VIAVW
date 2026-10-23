#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <string>
#include <vector>
#include <algorithm>

static int fail(const char* message) {
    DWORD written; WriteFile(GetStdHandle(STD_ERROR_HANDLE), message, (DWORD)strlen(message), &written, nullptr); return 2;
}
static std::wstring quote(const std::wstring& value) {
    std::wstring result=L"\""; size_t slashes=0;
    for(wchar_t c:value) {
        if(c==L'\\') { ++slashes; continue; }
        result.append(slashes*(c==L'"'?2:1),L'\\');slashes=0;
        if(c==L'"') result+=L'\\'; result+=c;
    }
    result.append(slashes*2,L'\\'); return result+L'"';
}
int wmain() {
    std::vector<wchar_t> module(32768); DWORD count=GetModuleFileNameW(nullptr,module.data(),(DWORD)module.size());
    if(!count||count>=module.size())return fail("VIAVW: launcher location unavailable.\n");
    std::wstring root(module.data(),count);root.resize(root.find_last_of(L"\\/")+1);
    HANDLE config=CreateFileW((root+L"viavw-current.txt").c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(config==INVALID_HANDLE_VALUE)return fail("VIAVW: install selection missing; repair the installation.\n");
    char bytes[256];DWORD read=0;BOOL ok=ReadFile(config,bytes,sizeof(bytes),&read,nullptr);CloseHandle(config);
    if(!ok||read==sizeof(bytes))return fail("VIAVW: invalid install selection.\n");
    std::string selection(bytes,read);while(!selection.empty()&&(selection.back()=='\r'||selection.back()=='\n'))selection.pop_back();
    const std::string prefix="releases\\tool-",suffix="\\viavw.exe";
    if(selection.size()<=prefix.size()+suffix.size()||selection.compare(0,prefix.size(),prefix)||selection.compare(selection.size()-suffix.size(),suffix.size(),suffix))return fail("VIAVW: invalid release path.\n");
    for(size_t i=prefix.size();i<selection.size()-suffix.size();++i)if(!(selection[i]>='a'&&selection[i]<='z')&&!(selection[i]>='0'&&selection[i]<='9')&&selection[i]!='-')return fail("VIAVW: invalid release name.\n");
    std::wstring executable=root+std::wstring(selection.begin(),selection.end());
    if(GetFileAttributesW(executable.c_str())==INVALID_FILE_ATTRIBUTES)return fail("VIAVW: selected tool is missing; repair the installation.\n");
    int argc=0;auto argv=CommandLineToArgvW(GetCommandLineW(),&argc);if(!argv)return fail("VIAVW: arguments unavailable.\n");
    std::wstring command=quote(executable);for(int i=1;i<argc;++i)command+=L" "+quote(argv[i]);LocalFree(argv);
    STARTUPINFOEXW start{};start.StartupInfo.cb=sizeof(start);start.StartupInfo.dwFlags=STARTF_USESTDHANDLES;
    std::vector<HANDLE> inherited;DWORD ids[]={STD_INPUT_HANDLE,STD_OUTPUT_HANDLE,STD_ERROR_HANDLE};
    HANDLE* destinations[]={&start.StartupInfo.hStdInput,&start.StartupInfo.hStdOutput,&start.StartupInfo.hStdError};
    for(int i=0;i<3;++i) {
        HANDLE source=GetStdHandle(ids[i]),copy=nullptr;
        if(source&&source!=INVALID_HANDLE_VALUE) {
            if(!DuplicateHandle(GetCurrentProcess(),source,GetCurrentProcess(),&copy,0,TRUE,DUPLICATE_SAME_ACCESS)) {
                for(auto handle:inherited)CloseHandle(handle);return fail("VIAVW: could not forward standard handles.\n");
            }
            inherited.push_back(copy);
        }
        *destinations[i]=copy;
    }
    SIZE_T size=0;InitializeProcThreadAttributeList(nullptr,1,0,&size);std::vector<unsigned char> attributes(size);
    start.lpAttributeList=(LPPROC_THREAD_ATTRIBUTE_LIST)attributes.data();
    if(!InitializeProcThreadAttributeList(start.lpAttributeList,1,0,&size))return fail("VIAVW: process setup failed.\n");
    bool configured=inherited.empty()||UpdateProcThreadAttribute(start.lpAttributeList,0,PROC_THREAD_ATTRIBUTE_HANDLE_LIST,inherited.data(),inherited.size()*sizeof(HANDLE),nullptr,nullptr);
    PROCESS_INFORMATION process{};
    BOOL created=configured&&CreateProcessW(executable.c_str(),command.data(),nullptr,nullptr,!inherited.empty(),EXTENDED_STARTUPINFO_PRESENT|CREATE_NO_WINDOW,nullptr,root.c_str(),&start.StartupInfo,&process);
    DeleteProcThreadAttributeList(start.lpAttributeList);for(auto handle:inherited)CloseHandle(handle);
    if(!created)return fail("VIAVW: selected tool could not start.\n");
    CloseHandle(process.hThread);WaitForSingleObject(process.hProcess,INFINITE);DWORD result=2;GetExitCodeProcess(process.hProcess,&result);CloseHandle(process.hProcess);return (int)result;
}
