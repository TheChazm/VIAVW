#pragma once
#include <windows.h>
#include <cstdint>
#include <cstddef>
// Same-bitness, same-session research protocol. Never an arbitrary-process API.
struct alignas(8) BridgeBlock {
  uint32_t magic=0x56494156, version=1, pid=0, reserved=0;
  uint64_t window=0;
  volatile LONG initialized=0, requested=0, completed=0, error=0;
  uint32_t width=0,height=0,format=0,generation=0;
  uint64_t texture=0;
  LUID adapter{};
  volatile LONG copied=0, skipped=0, presents=0, activeKeys=0;
  BYTE keys[256]{};
  uint64_t sourceLocalBytes=0,sourceNonLocalBytes=0;
  volatile LONG memorySupported=0;
  // Appended fields keep the accepted reader's original layout intact.
  volatile LONG acknowledged=0,present1Calls=0,resizeCalls=0,sourceDevices=0,keyAdapterInstalled=0;
  volatile LONG cursorGuardInstalled=0,cursorClipRequests=0,cursorWarpRequests=0;
};
static_assert(offsetof(BridgeBlock,window)==16 && offsetof(BridgeBlock,keys)==88 && offsetof(BridgeBlock,sourceLocalBytes)==344 && offsetof(BridgeBlock,acknowledged)==364 && offsetof(BridgeBlock,cursorGuardInstalled)==384);
struct BridgeInit { wchar_t mapping[128]; uint32_t pid; uint32_t reserved; uint64_t window; };
