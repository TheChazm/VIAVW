#include "ModuleSnapshot.h"
#include <iostream>

static int mode=0,calls=0;
static HANDLE WINAPI create(DWORD flags,DWORD pid){
  ++calls;
  if((mode==1 && calls<=3) || mode==2){SetLastError(ERROR_BAD_LENGTH);return INVALID_HANDLE_VALUE;}
  if(mode==3){SetLastError(ERROR_ACCESS_DENIED);return INVALID_HANDLE_VALUE;}
  return CreateToolhelp32Snapshot(flags,pid);
}
int main(){
  bool transient=false,denied=false,bounded=false,handlesStable=false;unsigned retries=0;double boundedMs=0;
  mode=1;calls=0;
  HANDLE snapshot=boundedModuleSnapshot(GetCurrentProcessId(),retries,create);MODULEENTRY32W entry{};entry.dwSize=sizeof(entry);
  transient=retries==3 && calls==4 && Module32FirstW(snapshot,&entry);CloseHandle(snapshot);
  mode=3;calls=0;retries=0;
  try{snapshot=boundedModuleSnapshot(GetCurrentProcessId(),retries,create);CloseHandle(snapshot);}catch(HRESULT result){denied=result==HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED) && calls==1 && retries==0;}
  mode=2;calls=0;retries=0;auto start=std::chrono::steady_clock::now();
  try{snapshot=boundedModuleSnapshot(GetCurrentProcessId(),retries,create);CloseHandle(snapshot);}catch(HRESULT result){boundedMs=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();bounded=result==HRESULT_FROM_WIN32(ERROR_BAD_LENGTH) && retries>0 && boundedMs>=500 && boundedMs<2000;}
  DWORD before=0,after=0;if(!GetProcessHandleCount(GetCurrentProcess(),&before))return 2;
  for(int index=0;index<64;index++){snapshot=boundedModuleSnapshot(GetCurrentProcessId(),retries);CloseHandle(snapshot);}
  if(!GetProcessHandleCount(GetCurrentProcess(),&after))return 2;handlesStable=before==after;
  bool passed=transient&&denied&&bounded&&handlesStable;
  std::cout<<"{\"passed\":"<<(passed?"true":"false")<<",\"transientRetriesThenRealSnapshot\":"<<(transient?"true":"false")<<",\"accessDeniedNotRetried\":"<<(denied?"true":"false")<<",\"persistentFailureBounded\":"<<(bounded?"true":"false")<<",\"persistentFailureMs\":"<<boundedMs<<",\"handlesStable\":"<<(handlesStable?"true":"false")<<"}"<<std::endl;
  return passed?0:1;
}
