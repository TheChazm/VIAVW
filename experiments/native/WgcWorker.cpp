#include <windows.h>
#include <d3d11.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <wincodec.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <chrono>
#include <mutex>
#include <memory>
#include <sstream>
#include <string>
using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
using Clock=std::chrono::steady_clock;
double milliseconds(Clock::time_point start) { return std::chrono::duration<double,std::milli>(Clock::now()-start).count(); }
std::string escaped(std::string const& value) {
  std::string result; for(unsigned char c:value) { if(c=='"'||c=='\\') {result+='\\';result+=c;} else if(c<32) {char b[7];sprintf_s(b,"\\u%04x",c);result+=b;} else result+=c; } return result;
}
std::string desktopName(DWORD thread) {
  wchar_t name[256]{}; DWORD size; check_bool(GetUserObjectInformationW(GetThreadDesktop(thread),UOI_NAME,name,sizeof(name),&size)); return to_string(name);
}
double cpuMs() { FILETIME created,exited,kernel,user; check_bool(GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user)); ULARGE_INTEGER k{},u{};k.LowPart=kernel.dwLowDateTime;k.HighPart=kernel.dwHighDateTime;u.LowPart=user.dwLowDateTime;u.HighPart=user.dwHighDateTime;return (k.QuadPart+u.QuadPart)/10000.0; }
struct Received {
  std::mutex gate; HANDLE event=CreateEventW(nullptr,TRUE,FALSE,nullptr); Direct3D11CaptureFrame frame{nullptr}; bool accepting=true;
  Received() {check_bool(event!=nullptr);} ~Received(){CloseHandle(event);}
};
struct Engine {
  com_ptr<ID3D11Device> device; com_ptr<ID3D11DeviceContext> context;
  IDirect3DDevice direct{nullptr}; com_ptr<IWICImagingFactory> wic; com_ptr<ID3D11Texture2D> staging;
  GraphicsCaptureItem item{nullptr}; Direct3D11CaptureFramePool pool{nullptr}; HWND cachedWindow=nullptr; DWORD cachedPid=0; int width=0,height=0;
  unsigned sequence=0,deviceCreations=0,poolCreations=0,stagingCreations=0; double initializeMs=0;
  void initialize() {
    if(device) return; auto start=Clock::now();
    if(!GraphicsCaptureSession::IsSupported()) throw hresult_error(E_NOTIMPL,L"WGC reports unsupported");
    D3D_FEATURE_LEVEL level; check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,device.put(),&level,context.put()));
    auto dxgi=device.as<IDXGIDevice>();com_ptr<::IInspectable> inspectable;check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(),inspectable.put()));direct=inspectable.as<IDirect3DDevice>();
    check_hresult(CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(wic.put()))); deviceCreations++;initializeMs=milliseconds(start);
  }
  void save(ID3D11Texture2D* texture,std::wstring const& path) {
    D3D11_TEXTURE2D_DESC desc{}; texture->GetDesc(&desc); D3D11_TEXTURE2D_DESC previous{}; if(staging)staging->GetDesc(&previous);
    if(!staging||previous.Width!=desc.Width||previous.Height!=desc.Height||previous.Format!=desc.Format) {
      staging=nullptr;desc.BindFlags=0;desc.MiscFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;check_hresult(device->CreateTexture2D(&desc,nullptr,staging.put()));stagingCreations++;
    }
    context->CopyResource(staging.get(),texture);D3D11_MAPPED_SUBRESOURCE mapped{};check_hresult(context->Map(staging.get(),0,D3D11_MAP_READ,0,&mapped));
    try {
      com_ptr<IWICStream> stream;check_hresult(wic->CreateStream(stream.put()));check_hresult(stream->InitializeFromFilename(path.c_str(),GENERIC_WRITE));
      com_ptr<IWICBitmapEncoder> encoder;check_hresult(wic->CreateEncoder(GUID_ContainerFormatPng,nullptr,encoder.put()));check_hresult(encoder->Initialize(stream.get(),WICBitmapEncoderNoCache));
      com_ptr<IWICBitmapFrameEncode> output;check_hresult(encoder->CreateNewFrame(output.put(),nullptr));check_hresult(output->Initialize(nullptr));check_hresult(output->SetSize(desc.Width,desc.Height));
      WICPixelFormatGUID format=GUID_WICPixelFormat32bppBGRA;check_hresult(output->SetPixelFormat(&format));check_hresult(output->WritePixels(desc.Height,mapped.RowPitch,mapped.RowPitch*desc.Height,(BYTE*)mapped.pData));check_hresult(output->Commit());check_hresult(encoder->Commit());
    } catch(...) {context->Unmap(staging.get(),0);throw;} context->Unmap(staging.get(),0);
  }
  std::string capture(HWND window,DWORD expectedPid,int requestedWidth,int requestedHeight,std::wstring const& path) {
    auto start=Clock::now(); double cpuBefore=cpuMs();const char* stage="validate";int itemWidth=0,itemHeight=0;
    try {
      if(!IsWindow(window))throw hresult_error(E_INVALIDARG,L"Source HWND is not valid on worker desktop");
      DWORD pid;DWORD thread=GetWindowThreadProcessId(window,&pid);
      if(pid!=expectedPid||desktopName(thread)!=desktopName(GetCurrentThreadId()))throw hresult_error(E_ACCESSDENIED,L"Source PID/desktop mismatch");
      stage="initialize device";initialize();stage="CreateForWindow";
      if(cachedWindow!=window||cachedPid!=pid||!item) {
        if(pool){pool.Close();pool=nullptr;} item=nullptr;cachedWindow=nullptr;
        auto interop=get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();check_hresult(interop->CreateForWindow(window,guid_of<GraphicsCaptureItem>(),put_abi(item)));cachedWindow=window;cachedPid=pid;
      }
      auto size=item.Size();itemWidth=size.Width;itemHeight=size.Height;
      if(size.Width<=0||size.Height<=0){size.Width=requestedWidth;size.Height=requestedHeight;}
      if(size.Width<=0||size.Height<=0||size.Width>8192||size.Height>8192||(int64_t)size.Width*size.Height>20000000)throw hresult_error(E_INVALIDARG,L"Invalid bounded capture size");
      stage="CreateFreeThreaded";
      // Reopening a retained pool produced E_UNEXPECTED in rapid capture experiments.
      // Keep the expensive device/item/readback resources; give each session a fresh pool.
      if(pool){pool.Close();pool=nullptr;}
      if(!pool){pool=Direct3D11CaptureFramePool::CreateFreeThreaded(direct,DirectXPixelFormat::B8G8R8A8UIntNormalized,2,size);poolCreations++;width=size.Width;height=size.Height;}
      auto state=std::make_shared<Received>();auto session=pool.CreateCaptureSession(item);session.IsCursorCaptureEnabled(false);
      auto token=pool.FrameArrived([state](auto const& sender,auto const&) {
        try {auto frame=sender.TryGetNextFrame();if(!frame)return;std::scoped_lock lock(state->gate);if(state->accepting&&!state->frame){state->frame=frame;SetEvent(state->event);}else frame.Close();}catch(hresult_error const&){}
      });
      DWORD waited;
      try {stage="StartCapture / first frame";session.StartCapture();waited=WaitForSingleObject(state->event,3000);}
      catch(...) {pool.FrameArrived(token);session.Close();throw;}
      pool.FrameArrived(token);session.Close();
      {std::scoped_lock lock(state->gate);state->accepting=false;}
      if(waited!=WAIT_OBJECT_0)throw hresult_error(HRESULT_FROM_WIN32(WAIT_TIMEOUT),L"No capture frame within 3000 ms");
      stage="readback / PNG";
      {std::scoped_lock lock(state->gate);auto access=state->frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();com_ptr<ID3D11Texture2D> texture;check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D),texture.put_void()));save(texture.get(),path);state->frame.Close();}
      pool.Close();pool=nullptr;
      sequence++;std::ostringstream out;out<<"{\"success\":true,\"milliseconds\":"<<milliseconds(start)<<",\"cpuMs\":"<<cpuMs()-cpuBefore<<",\"sequence\":"<<sequence<<",\"deviceCreations\":"<<deviceCreations<<",\"poolCreations\":"<<poolCreations<<",\"stagingCreations\":"<<stagingCreations<<",\"initializeMs\":"<<initializeMs<<",\"width\":"<<width<<",\"height\":"<<height<<",\"activeSessions\":0}";return out.str();
    } catch(hresult_error const& e) {
      // Dispose a potentially faulted pool/item, retaining the graphics device for later requests.
      if(pool){try{pool.Close();}catch(...){}pool=nullptr;}item=nullptr;cachedWindow=nullptr;
      std::ostringstream out;out<<"{\"success\":false,\"error\":\""<<escaped(std::string(stage)+" on desktop "+desktopName(GetCurrentThreadId())+" (item size "+std::to_string(itemWidth)+"x"+std::to_string(itemHeight)+"): "+to_string(e.message()))<<"\",\"hresult\":"<<(uint32_t)e.code()<<",\"activeSessions\":0}";return out.str();
    }
  }
  ~Engine(){if(pool){try{pool.Close();}catch(...){}}}
};
bool line(HANDLE pipe,std::string& value) {value.clear();char c;DWORD bytes;while(ReadFile(pipe,&c,1,&bytes,nullptr)&&bytes){if(c=='\n')return true;if(value.size()>32768)return false;value+=c;}return false;}
void write(HANDLE pipe,std::string const& value){std::string packet=value+"\n";DWORD written;check_bool(WriteFile(pipe,packet.data(),(DWORD)packet.size(),&written,nullptr));check_bool(written==packet.size());}
int wmain(int argc,wchar_t** argv) {
  if((argc!=4&&argc!=5)||wcscmp(argv[1],L"--pipe")!=0)return 1;
  bool testHang=argc==5&&wcscmp(argv[4],L"--test-fixture")==0;
  init_apartment(apartment_type::multi_threaded);std::wstring name=L"\\\\.\\pipe\\"+std::wstring(argv[2]);
  HANDLE pipe=CreateFileW(name.c_str(),GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr);if(pipe==INVALID_HANDLE_VALUE)return 2;
  try {
    write(pipe,"{\"token\":\""+to_string(argv[3])+"\",\"pid\":"+std::to_string(GetCurrentProcessId())+",\"desktop\":\""+escaped(desktopName(GetCurrentThreadId()))+"\"}");
    Engine engine;std::string packet;
    while(line(pipe,packet)) {
      if(packet=="quit")break;
      if(packet=="hang"&&testHang){Sleep(INFINITE);continue;}
      std::istringstream input(packet);std::string hwnd,pid,width,height,path;
      if(!std::getline(input,hwnd,'\t')||!std::getline(input,pid,'\t')||!std::getline(input,width,'\t')||!std::getline(input,height,'\t')||!std::getline(input,path)||path.empty()){write(pipe,"{\"success\":false,\"error\":\"Malformed bounded request\"}");continue;}
      write(pipe,engine.capture((HWND)_strtoui64(hwnd.c_str(),nullptr,10),(DWORD)strtoul(pid.c_str(),nullptr,10),atoi(width.c_str()),atoi(height.c_str()),to_hstring(path).c_str()));
    }
  }catch(...){CloseHandle(pipe);return 3;}CloseHandle(pipe);return 0;
}
