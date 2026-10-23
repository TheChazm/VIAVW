#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <wincodec.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <fstream>
#include <filesystem>
#include <chrono>
#include <mutex>
using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;

void png(ID3D11Device* device,ID3D11DeviceContext* context,ID3D11Texture2D* texture,const wchar_t* path) {
  D3D11_TEXTURE2D_DESC desc{}; texture->GetDesc(&desc); desc.BindFlags=0; desc.MiscFlags=0; desc.Usage=D3D11_USAGE_STAGING; desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
  com_ptr<ID3D11Texture2D> staging; check_hresult(device->CreateTexture2D(&desc,nullptr,staging.put())); context->CopyResource(staging.get(),texture);
  D3D11_MAPPED_SUBRESOURCE mapped{}; check_hresult(context->Map(staging.get(),0,D3D11_MAP_READ,0,&mapped));
  try {
    com_ptr<IWICImagingFactory> factory; check_hresult(CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(factory.put())));
    com_ptr<IWICStream> stream; check_hresult(factory->CreateStream(stream.put())); check_hresult(stream->InitializeFromFilename(path,GENERIC_WRITE));
    com_ptr<IWICBitmapEncoder> encoder; check_hresult(factory->CreateEncoder(GUID_ContainerFormatPng,nullptr,encoder.put())); check_hresult(encoder->Initialize(stream.get(),WICBitmapEncoderNoCache));
    com_ptr<IWICBitmapFrameEncode> frame; check_hresult(encoder->CreateNewFrame(frame.put(),nullptr)); check_hresult(frame->Initialize(nullptr)); check_hresult(frame->SetSize(desc.Width,desc.Height));
    WICPixelFormatGUID format=GUID_WICPixelFormat32bppBGRA; check_hresult(frame->SetPixelFormat(&format)); check_hresult(frame->WritePixels(desc.Height,mapped.RowPitch,mapped.RowPitch*desc.Height,(BYTE*)mapped.pData)); check_hresult(frame->Commit()); check_hresult(encoder->Commit());
  } catch(...) { context->Unmap(staging.get(),0); throw; }
  context->Unmap(staging.get(),0);
}
int wmain(int argc,wchar_t** argv) {
  if(argc!=3 && argc!=5) return 1;
  auto start=std::chrono::steady_clock::now();
  const char* stage="initialize";
  int itemWidth=0,itemHeight=0;
  try {
    init_apartment(apartment_type::multi_threaded);
    auto initialized=std::chrono::steady_clock::now();
    stage="IsSupported"; if(!GraphicsCaptureSession::IsSupported()) throw hresult_error(E_NOTIMPL,L"WGC reports unsupported");
    HWND hwnd=(HWND)_wcstoui64(argv[1],nullptr,10);
    auto interop=get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();
    stage="CreateForWindow"; GraphicsCaptureItem item{nullptr}; check_hresult(interop->CreateForWindow(hwnd,guid_of<GraphicsCaptureItem>(),put_abi(item)));
    auto itemCreated=std::chrono::steady_clock::now();
    com_ptr<ID3D11Device> device; com_ptr<ID3D11DeviceContext> context; D3D_FEATURE_LEVEL level;
    stage="D3D11CreateDevice"; check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,device.put(),&level,context.put()));
    auto dxgi=device.as<IDXGIDevice>(); com_ptr<::IInspectable> inspectable;
    check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(),inspectable.put())); auto direct=inspectable.as<IDirect3DDevice>();
    auto deviceCreated=std::chrono::steady_clock::now();
    auto size=item.Size(); itemWidth=size.Width; itemHeight=size.Height;
    if(size.Width<=0 || size.Height<=0) { RECT rect{}; GetWindowRect(hwnd,&rect); size.Width=argc==5?_wtoi(argv[3]):rect.right-rect.left; size.Height=argc==5?_wtoi(argv[4]):rect.bottom-rect.top; }
    stage="CreateFreeThreaded"; auto pool=Direct3D11CaptureFramePool::CreateFreeThreaded(direct,DirectXPixelFormat::B8G8R8A8UIntNormalized,2,size);
    auto session=pool.CreateCaptureSession(item); session.IsCursorCaptureEnabled(false);
    HANDLE event=CreateEventW(nullptr,TRUE,FALSE,nullptr); if(!event) throw hresult_error(HRESULT_FROM_WIN32(GetLastError()));
    std::mutex gate; Direct3D11CaptureFrame captured{nullptr};
    auto token=pool.FrameArrived([&](auto const& sender,auto const&){auto frame=sender.TryGetNextFrame(); if(frame){std::scoped_lock lock(gate); if(!captured) {captured=frame; SetEvent(event);} }});
    auto ready=std::chrono::steady_clock::now();
    stage="StartCapture / first frame"; session.StartCapture(); DWORD wait=WaitForSingleObject(event,3000);
    auto frameArrived=std::chrono::steady_clock::now();
    pool.FrameArrived(token); session.Close(); CloseHandle(event);
    if(wait!=WAIT_OBJECT_0){pool.Close(); throw hresult_error(HRESULT_FROM_WIN32(WAIT_TIMEOUT),L"No WGC frame within 3000 ms on this desktop");}
    stage="surface texture / PNG"; { std::scoped_lock lock(gate); auto access=captured.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>(); com_ptr<ID3D11Texture2D> texture; check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D),texture.put_void())); png(device.get(),context.get(),texture.get(),argv[2]); captured.Close(); }
    pool.Close(); auto finished=std::chrono::steady_clock::now();
    auto ms=[](auto end,auto begin){return std::chrono::duration<double,std::milli>(end-begin).count();};
    std::ofstream out(std::filesystem::path(argv[2]).replace_extension(L".json")); out<<"{\"method\":\"WindowsGraphicsCapture\",\"milliseconds\":"<<ms(finished,start)<<",\"success\":true,\"stagesMs\":{\"winrtInitialize\":"<<ms(initialized,start)<<",\"createItem\":"<<ms(itemCreated,initialized)<<",\"createDevice\":"<<ms(deviceCreated,itemCreated)<<",\"createPoolSession\":"<<ms(ready,deviceCreated)<<",\"startAndFirstFrame\":"<<ms(frameArrived,ready)<<",\"closeReadbackPng\":"<<ms(finished,frameArrived)<<"}}"; return 0;
  } catch(hresult_error const& e) {
    wchar_t desktop[256]{}; DWORD needed; GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()),UOI_NAME,desktop,sizeof(desktop),&needed);
    std::ofstream out(std::filesystem::path(argv[2]).replace_extension(L".error.txt")); out<<stage<<" on desktop "<<to_string(desktop)<<" (item size "<<itemWidth<<"x"<<itemHeight<<"): HRESULT 0x"<<std::hex<<(uint32_t)e.code()<<": "<<to_string(e.message()); return 2;
  }
}
