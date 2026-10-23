#include <windows.h>
#include <d3d11_1.h>
#include <dxgi1_2.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <fstream>
#include <string>
using Microsoft::WRL::ComPtr;

// Ordinary renderer/input fixture. No bridge, shared texture or wrapper protocol.
static ComPtr<ID3D11Device> device;
static ComPtr<ID3D11DeviceContext> context;
static ComPtr<ID3D11DeviceContext1> context1;
static ComPtr<IDXGISwapChain1> swap;
static ComPtr<ID3D11RenderTargetView> target;
static ComPtr<ID3D11VertexShader> vertex;
static ComPtr<ID3D11PixelShader> pixel;
static ComPtr<ID3D11Buffer> colors;
static UINT width=620,height=440;
static int frames=0,clicks=0,shortcuts=0,resizes=0,devices=0;
static bool present1=true,asyncCtrl=false,closed=false;
static HRESULT presentResult=S_OK,resizeResult=S_OK;
static void check(HRESULT value){if(FAILED(value))throw value;}
static void observe(){
  std::ofstream out("observed.json.tmp");
  out<<"{\"pid\":"<<GetCurrentProcessId()<<",\"frames\":"<<frames<<",\"clicks\":"<<clicks<<",\"shortcuts\":"<<shortcuts<<",\"asyncCtrlState\":"<<(asyncCtrl?"true":"false")<<",\"width\":"<<width<<",\"height\":"<<height<<",\"resizes\":"<<resizes<<",\"devices\":"<<devices<<",\"present1\":"<<(present1?"true":"false")<<",\"closed\":"<<(closed?"true":"false")<<",\"presentHresult\":"<<(long)presentResult<<",\"resizeHresult\":"<<(long)resizeResult<<"}";
  out.close();MoveFileExW(L"observed.json.tmp",L"observed.json",MOVEFILE_REPLACE_EXISTING);
}
static void makeTarget(){ComPtr<ID3D11Texture2D> back;check(swap->GetBuffer(0,IID_PPV_ARGS(&back)));check(device->CreateRenderTargetView(back.Get(),nullptr,&target));}
static void render(){
  if(!target)return;
  float blue[]={.06f,.12f,.24f,1};context->ClearRenderTargetView(target.Get(),blue);
  float color[]={clicks%2?.95f:.15f,clicks%2?.55f:.70f,clicks%2?.15f:.40f,1};context->UpdateSubresource(colors.Get(),0,nullptr,color,0,0);
  ID3D11RenderTargetView* output=target.Get();context->OMSetRenderTargets(1,&output,nullptr);
  D3D11_VIEWPORT viewport{0,0,(float)width,(float)height,0,1};context->RSSetViewports(1,&viewport);
  context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);context->VSSetShader(vertex.Get(),nullptr,0);context->PSSetShader(pixel.Get(),nullptr,0);
  ID3D11Buffer* buffer=colors.Get();context->PSSetConstantBuffers(0,1,&buffer);context->Draw(3,0);frames++;
  // The actual GPU image carries its frame number, independent of JSON telemetry.
  for(int bit=0;bit<16;bit++){float value=(frames&(1<<bit))?1.0f:0.0f;float ink[]={value,value,value,1};D3D11_RECT cell{10+bit*10,(LONG)height-20,18+bit*10,(LONG)height-12};context1->ClearView(target.Get(),ink,&cell,1);}
  if(present1){DXGI_PRESENT_PARAMETERS parameters{};presentResult=swap->Present1(0,0,&parameters);}else presentResult=swap->Present(0,0);
  observe();
}
static void createDevice(HWND window){
  if(context)context->ClearState();target.Reset();swap.Reset();if(context)context->Flush();
  colors.Reset();pixel.Reset();vertex.Reset();context1.Reset();context.Reset();device.Reset();
  D3D_FEATURE_LEVEL level;check(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,&level,&context));check(context.As(&context1));
  ComPtr<IDXGIDevice> dxgi;ComPtr<IDXGIAdapter> adapter;ComPtr<IDXGIFactory2> factory;check(device.As(&dxgi));check(dxgi->GetAdapter(&adapter));check(adapter->GetParent(IID_PPV_ARGS(&factory)));
  DXGI_SWAP_CHAIN_DESC1 desc{};desc.Width=width;desc.Height=height;desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.BufferCount=2;desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
  check(factory->CreateSwapChainForHwnd(device.Get(),window,&desc,nullptr,nullptr,&swap));check(factory->MakeWindowAssociation(window,DXGI_MWA_NO_ALT_ENTER));makeTarget();
  const char* shader="cbuffer C:register(b0){float4 color;} float4 VS(uint id:SV_VertexID):SV_POSITION{float2 p[3]={float2(-.8,-.8),float2(0,.8),float2(.8,-.8)};return float4(p[id],0,1);} float4 PS():SV_TARGET{return color;}";
  ComPtr<ID3DBlob> v,p,error;check(D3DCompile(shader,strlen(shader),nullptr,nullptr,nullptr,"VS","vs_4_0",0,0,&v,&error));check(D3DCompile(shader,strlen(shader),nullptr,nullptr,nullptr,"PS","ps_4_0",0,0,&p,&error));check(device->CreateVertexShader(v->GetBufferPointer(),v->GetBufferSize(),nullptr,&vertex));check(device->CreatePixelShader(p->GetBufferPointer(),p->GetBufferSize(),nullptr,&pixel));
  D3D11_BUFFER_DESC buffer{};buffer.ByteWidth=16;buffer.Usage=D3D11_USAGE_DEFAULT;buffer.BindFlags=D3D11_BIND_CONSTANT_BUFFER;check(device->CreateBuffer(&buffer,nullptr,&colors));devices++;
}
static void sizeWindow(HWND window,UINT w,UINT h){RECT rect{0,0,(LONG)w,(LONG)h};AdjustWindowRect(&rect,WS_OVERLAPPEDWINDOW,FALSE);SetWindowPos(window,nullptr,0,0,rect.right-rect.left,rect.bottom-rect.top,SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE);}
static LRESULT CALLBACK proc(HWND window,UINT message,WPARAM key,LPARAM point){
  try{
    switch(message){
      case WM_SIZE:
        if(swap && LOWORD(point) && HIWORD(point)){width=LOWORD(point);height=HIWORD(point);context->ClearState();context->Flush();target.Reset();resizeResult=swap->ResizeBuffers(0,width,height,DXGI_FORMAT_UNKNOWN,0);check(resizeResult);makeTarget();resizes++;render();}return 0;
      case WM_TIMER:render();return 0;
      case WM_PAINT:{PAINTSTRUCT paint;BeginPaint(window,&paint);EndPaint(window,&paint);render();return 0;}
      case WM_KEYDOWN:
        asyncCtrl=(GetAsyncKeyState(VK_CONTROL)&0x8000)!=0;
        if(key=='K' && (GetKeyState(VK_CONTROL)&0x8000))shortcuts++;
        if(key==VK_SPACE)clicks++;
        if(key=='P')present1=!present1;
        if(key=='R')createDevice(window);
        if(key==VK_F5)sizeWindow(window,800,600);
        if(key==VK_F6)sizeWindow(window,480,320);
        if(key==VK_F7)sizeWindow(window,1024,576);
        if(key==VK_F8)sizeWindow(window,620,440);
        render();return 0;
      case WM_DESTROY:closed=true;observe();PostQuitMessage(0);return 0;
    }
  }catch(HRESULT failure){std::ofstream("render-error.txt")<<std::hex<<failure;DestroyWindow(window);return 0;}
  return DefWindowProcW(window,message,key,point);
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,LPWSTR,int){
  SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
  try{WNDCLASSW info{};info.hInstance=instance;info.lpfnWndProc=proc;info.lpszClassName=L"VIAVW_DynamicD3DProbe";RegisterClassW(&info);RECT rect{0,0,620,440};AdjustWindowRect(&rect,WS_OVERLAPPEDWINDOW,FALSE);
    HWND window=CreateWindowExW(0,info.lpszClassName,L"Disposable dynamic Direct3D application",WS_OVERLAPPEDWINDOW,40,40,rect.right-rect.left,rect.bottom-rect.top,nullptr,nullptr,instance,nullptr);if(!window)return 2;createDevice(window);ShowWindow(window,SW_SHOWNOACTIVATE);SetTimer(window,1,100,nullptr);render();
    MSG message;while(GetMessageW(&message,nullptr,0,0)>0){TranslateMessage(&message);DispatchMessageW(&message);}return 0;
  }catch(HRESULT error){std::ofstream("startup-error.txt")<<std::hex<<error;return 1;}
}
