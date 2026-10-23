#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <string>
using Microsoft::WRL::ComPtr;

ComPtr<ID3D11Device> device;
ComPtr<ID3D11DeviceContext> context;
ComPtr<IDXGISwapChain> swap;
ComPtr<ID3D11RenderTargetView> target;
ComPtr<ID3D11VertexShader> vs;
ComPtr<ID3D11PixelShader> ps;
ComPtr<ID3D11Buffer> colorBuffer;
std::wstring text;
int clicks=0, keys=0, frames=0, ticks=0, wheel=0, moves=0, drags=0, shortcuts=0;
bool ctrlState=false, asyncCtrlState=false, closed=false;
HRESULT presentResult=S_OK;
std::wstring adapterName;
void check(HRESULT result) { if (FAILED(result)) throw result; }
std::string utf8(std::wstring const& value) { int n=WideCharToMultiByte(CP_UTF8,0,value.data(),(int)value.size(),nullptr,0,nullptr,nullptr); std::string out(n,0); WideCharToMultiByte(CP_UTF8,0,value.data(),(int)value.size(),out.data(),n,nullptr,nullptr); return out; }
std::string json(std::string const& s) { std::string out="\""; for (char c:s) { if(c=='"'||c=='\\') out+='\\'; if(c=='\n') out+="\\n"; else if(c=='\r') out+="\\r"; else if((unsigned char)c>=32) out+=c; } return out+'"'; }
void writeState() {
  std::ofstream out("observed.json.tmp");
  out << "{\"framework\":\"Direct3D11\",\"pid\":"<<GetCurrentProcessId()<<",\"ticks\":"<<ticks<<",\"frames\":"<<frames<<",\"clicks\":"<<clicks<<",\"keyEvents\":"<<keys<<",\"wheel\":"<<wheel<<",\"moves\":"<<moves<<",\"drags\":"<<drags<<",\"shortcuts\":"<<shortcuts<<",\"ctrlState\":"<<(ctrlState?"true":"false")<<",\"asyncCtrlState\":"<<(asyncCtrlState?"true":"false")<<",\"closed\":"<<(closed?"true":"false")<<",\"presentHresult\":"<<(long)presentResult<<",\"adapter\":"<<json(utf8(adapterName))<<",\"text\":"<<json(utf8(text))<<"}";
  out.close(); MoveFileExW(L"observed.json.tmp",L"observed.json",MOVEFILE_REPLACE_EXISTING);
}
void render() {
  if(!target) return;
  float background[]={0.06f,0.12f,0.24f,1}; context->ClearRenderTargetView(target.Get(),background);
  float color[]={clicks%2?0.95f:0.15f,clicks%2?0.55f:0.70f,clicks%2?0.15f:0.40f,1};
  context->UpdateSubresource(colorBuffer.Get(),0,nullptr,color,0,0);
  ID3D11RenderTargetView* rt=target.Get(); context->OMSetRenderTargets(1,&rt,nullptr);
  D3D11_VIEWPORT viewport{0,0,620,440,0,1}; context->RSSetViewports(1,&viewport);
  context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
  context->VSSetShader(vs.Get(),nullptr,0); context->PSSetShader(ps.Get(),nullptr,0);
  ID3D11Buffer* cb=colorBuffer.Get(); context->PSSetConstantBuffers(0,1,&cb);
  context->Draw(3,0); frames++; presentResult=swap->Present(0,0);
}
LRESULT CALLBACK proc(HWND hwnd,UINT message,WPARAM wp,LPARAM lp) {
  switch(message) {
    case WM_TIMER: ticks++; render(); writeState(); return 0;
    case WM_CHAR: if(wp==8 && !text.empty()) text.pop_back(); else if(wp>=32) text+=(wchar_t)wp; render(); writeState(); return 0;
    case WM_KEYDOWN: keys++; ctrlState=(GetKeyState(VK_CONTROL)&0x8000)!=0; asyncCtrlState=(GetAsyncKeyState(VK_CONTROL)&0x8000)!=0; if(wp=='K'&&ctrlState) shortcuts++; if(wp==VK_SPACE) { clicks++; render(); } writeState(); return 0;
    case WM_LBUTTONDOWN: clicks++; SetCapture(hwnd); render(); writeState(); return 0;
    case WM_LBUTTONUP: ReleaseCapture(); return 0;
    case WM_MOUSEMOVE: moves++; if(wp&MK_LBUTTON) drags++; writeState(); return 0;
    case WM_MOUSEWHEEL: wheel+=(short)HIWORD(wp); writeState(); return 0;
    case WM_PAINT: { PAINTSTRUCT paint; BeginPaint(hwnd,&paint); EndPaint(hwnd,&paint); render(); return 0; }
    case WM_DESTROY: closed=true; writeState(); PostQuitMessage(0); return 0;
  }
  return DefWindowProcW(hwnd,message,wp,lp);
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,LPWSTR command,int) {
  SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
  try {
    WNDCLASSW wc{}; wc.hInstance=instance; wc.lpfnWndProc=proc; wc.lpszClassName=L"VIAVW_D3DProbe"; wc.style=CS_DBLCLKS; wc.hCursor=LoadCursor(nullptr,IDC_ARROW); RegisterClassW(&wc);
    RECT rect{0,0,620,440}; AdjustWindowRect(&rect,WS_OVERLAPPEDWINDOW,FALSE);
    HWND hwnd=CreateWindowExW(0,wc.lpszClassName,L"Disposable Direct3D application",WS_OVERLAPPEDWINDOW,40,40,rect.right-rect.left,rect.bottom-rect.top,nullptr,nullptr,instance,nullptr);
    DXGI_SWAP_CHAIN_DESC desc{}; desc.BufferDesc.Width=620; desc.BufferDesc.Height=440; desc.BufferDesc.Format=DXGI_FORMAT_B8G8R8A8_UNORM; desc.SampleDesc.Count=1; desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT; desc.BufferCount=2; desc.OutputWindow=hwnd; desc.Windowed=TRUE; desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
    D3D_FEATURE_LEVEL level;
    // Hardware only: initialization failure is evidence, never silently labeled GPU rendering.
    check(D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&desc,&swap,&device,&level,&context));
    ComPtr<IDXGIDevice> dxgi; check(device.As(&dxgi)); ComPtr<IDXGIAdapter> adapter; check(dxgi->GetAdapter(&adapter)); DXGI_ADAPTER_DESC info{}; adapter->GetDesc(&info); adapterName=info.Description;
    ComPtr<ID3D11Texture2D> back; check(swap->GetBuffer(0,IID_PPV_ARGS(&back))); check(device->CreateRenderTargetView(back.Get(),nullptr,&target));
    const char* shader="cbuffer C:register(b0){float4 color;} float4 VS(uint id:SV_VertexID):SV_POSITION{float2 p[3]={float2(-.8,-.8),float2(0,.8),float2(.8,-.8)};return float4(p[id],0,1);} float4 PS():SV_TARGET{return color;}";
    ComPtr<ID3DBlob> v,p,error; check(D3DCompile(shader,strlen(shader),nullptr,nullptr,nullptr,"VS","vs_4_0",0,0,&v,&error)); check(D3DCompile(shader,strlen(shader),nullptr,nullptr,nullptr,"PS","ps_4_0",0,0,&p,&error));
    check(device->CreateVertexShader(v->GetBufferPointer(),v->GetBufferSize(),nullptr,&vs)); check(device->CreatePixelShader(p->GetBufferPointer(),p->GetBufferSize(),nullptr,&ps));
    D3D11_BUFFER_DESC buffer{}; buffer.ByteWidth=16; buffer.Usage=D3D11_USAGE_DEFAULT; buffer.BindFlags=D3D11_BIND_CONSTANT_BUFFER; check(device->CreateBuffer(&buffer,nullptr,&colorBuffer));
    ShowWindow(hwnd,wcsstr(command,L"--noactivate")?SW_SHOWNOACTIVATE:SW_SHOW); SetTimer(hwnd,1,100,nullptr); render(); writeState();
    MSG message; while(GetMessageW(&message,nullptr,0,0)>0){TranslateMessage(&message);DispatchMessageW(&message);} return 0;
  } catch(HRESULT result) { std::ofstream("startup-error.txt")<<"Direct3D HRESULT "<<std::hex<<result; return 1; }
}
