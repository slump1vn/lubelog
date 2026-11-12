# App Android "Sói & Thỏ" (bản thử nghiệm)

App Android là một **Trusted Web Activity (TWA)**: một app nhỏ mở web app ở
`https://finance.digifusion.vn` toàn màn hình bằng Chrome trên điện thoại. Đây là cách Google
khuyến nghị để đưa một PWA lên Android (cũng là cách công cụ Bubblewrap và PWABuilder tạo ra).

Vì sao chọn TWA mà không viết app riêng hay bọc WebView:

- Mọi tính năng web đã có (chụp ảnh hoá đơn, tải tệp, đăng nhập) chạy ngay vì đó là Chrome thật,
  được Chrome tự cập nhật bảo mật.
- Sửa web là app cập nhật theo, không phải phát hành lại APK.
- Khi cần, cùng APK/AAB này đưa lên Google Play được.
- Không lưu số liệu tài chính trên điện thoại: service worker (`wwwroot/sw.js`) chỉ giữ trang
  "mất kết nối" và biểu tượng.

## Điều kiện

1. Web app chạy qua **HTTPS** với chứng chỉ hợp lệ (đã có: reverse proxy cho `finance.digifusion.vn`).
2. Điện thoại có Chrome (hoặc trình duyệt hỗ trợ TWA như Edge, Samsung Internet). Không có thì app tự
   chuyển sang WebView.
3. Máy chủ trả `/.well-known/assetlinks.json` chứa vân tay khoá ký của app. Thiếu bước này app vẫn chạy
   nhưng hiện một thanh địa chỉ nhỏ phía trên.

## Build APK

APK được build tự động bởi GitHub Actions (`.github/workflows/android.yml`) mỗi khi thư mục
`android/` thay đổi, hoặc bấm **Run workflow** ở tab Actions. Tải APK trong mục **Artifacts** của lần
chạy. Không cần cài Android SDK trên máy.

Khoá ký lấy từ 4 secret của repo (Settings > Secrets and variables > Actions):

| Secret | Nội dung |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | Tệp `.jks` mã hoá base64 |
| `ANDROID_KEYSTORE_PASSWORD` | Mật khẩu keystore |
| `ANDROID_KEY_ALIAS` | Alias của khoá |
| `ANDROID_KEY_PASSWORD` | Mật khẩu khoá |

Chưa đặt secret thì APK được ký bằng khoá debug tạm của mỗi lần build (cài thử được, nhưng vân tay đổi
mỗi lần nên không xác minh được tên miền). Phần tóm tắt của mỗi lần chạy in ra SHA-256 của khoá đã dùng.

**Giữ kỹ tệp `.jks` và mật khẩu.** Mất chúng thì không cập nhật được app đã cài; không bao giờ đưa vào repo.

Build trên máy có Android SDK: `cd android && ./gradlew assembleRelease`. Đổi tên miền bằng
`-PtwaHost=ten-mien-khac.vn` hoặc sửa `android/gradle.properties`.

## Cấu hình máy chủ

| Biến môi trường | Mặc định | Ý nghĩa |
|---|---|---|
| `LUBELOGGER_ANDROID_CERT_SHA256` | trống | Vân tay SHA-256 khoá ký app (dạng `AB:CD:...`), nhiều giá trị cách nhau bởi dấu phẩy |
| `LUBELOGGER_ANDROID_PACKAGE` | `vn.digifusion.finance` | Mã gói của app |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `false` | Đặt `true` khi chạy sau reverse proxy để app biết yêu cầu gốc là HTTPS |

Kiểm tra: mở `https://finance.digifusion.vn/.well-known/assetlinks.json` phải thấy vân tay, rồi thử bằng
công cụ của Google:
`https://digitalassetlinks.googleapis.com/v1/statements:list?source.web.site=https://finance.digifusion.vn&relation=delegate_permission/common.handle_all_urls`

Reverse proxy phải chuyển nguyên đường dẫn `/.well-known/assetlinks.json` tới app (không chuyển hướng,
không yêu cầu đăng nhập) và giữ kết nối WebSocket tới `/api/ws`.

## Cài lên điện thoại

1. Tải tệp `.apk` về điện thoại (từ GitHub hoặc chép qua cáp/Zalo).
2. Mở tệp, cho phép "Cài ứng dụng không rõ nguồn gốc" cho trình duyệt/trình quản lý tệp khi được hỏi.
3. Mở app **Sói & Thỏ**. Lần đầu có thể hiện thanh địa chỉ vài giây trong khi Android xác minh tên miền.

Google Play Protect có thể cảnh báo vì app chưa lên Play. Đây là bình thường với bản thử nghiệm.

## Mã nguồn

- `android/app/src/main/AndroidManifest.xml`: cấu hình TWA (URL, màu, màn hình chờ, liên kết mở bằng app).
- `android/app/build.gradle.kts`: mã gói, phiên bản (theo số lần chạy CI), cách ký.
- `android/tools/render_icons.py`: tạo biểu tượng từ `tools/branding/*.svg`.
