#pragma once
#include <windows.h>
#include <tlhelp32.h>
#include <chrono>

// A changing loader list may invalidate Toolhelp's required snapshot length.
// Retry only that documented transient error, with a finite per-snapshot bound.
using SnapshotFunction=HANDLE(WINAPI*)(DWORD,DWORD);
inline HANDLE boundedModuleSnapshot(DWORD pid,unsigned& retries,SnapshotFunction create=CreateToolhelp32Snapshot){
  auto deadline=std::chrono::steady_clock::now()+std::chrono::milliseconds(500);
  for(;;){
    HANDLE snapshot=create(TH32CS_SNAPMODULE|TH32CS_SNAPMODULE32,pid);
    if(snapshot!=INVALID_HANDLE_VALUE)return snapshot;
    DWORD error=GetLastError();
    if(error!=ERROR_BAD_LENGTH || std::chrono::steady_clock::now()>=deadline)throw HRESULT_FROM_WIN32(error);
    ++retries;Sleep(10);
  }
}
