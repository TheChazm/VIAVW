#pragma once
#include <windows.h>
#include <cstdint>
#include <cstddef>
struct alignas(8) QivGuardBlock {
  uint32_t magic=0x51495647,version=1,pid=0,reserved=0;
  uint64_t window=0;
  volatile LONG installed=0,clipRequests=0,warpRequests=0,error=0;
  uint64_t created=0;
  uint32_t clipSlots=0,warpSlots=0;
};
static_assert(offsetof(QivGuardBlock,window)==16 && offsetof(QivGuardBlock,installed)==24 && offsetof(QivGuardBlock,created)==40);
struct QivGuardInit { wchar_t mapping[128]; uint32_t pid=0,reserved=0; uint64_t window=0,created=0; };
