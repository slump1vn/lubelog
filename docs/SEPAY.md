# Theo dõi tài khoản chung qua SePay

Ứng dụng đọc giao dịch của **một tài khoản ngân hàng chung** của hai vợ chồng qua [SePay](https://developer.sepay.vn/vi) và hiện ở đầu tab **Ngân sách**: số dư, tiền vào và tiền ra từ đầu chu kỳ, 30 giao dịch gần nhất. Ứng dụng chỉ **đọc**; không thể chuyển tiền.

## Hai cách nhận giao dịch

| Cách | Cần gì | Độ trễ | Ghi chú |
|---|---|---|---|
| Kéo qua API (khuyến nghị bật) | Khoá API SePay | 15 phút (đổi bằng `LUBELOGGER_SEPAY_SYNC_MINUTES`) | Chạy được trong mạng nhà, không cần mở cổng. Bù lại webhook bị lỡ. |
| Webhook | Địa chỉ HTTPS công khai + secret HMAC | Vài giây | SePay chỉ gọi tên miền công khai, ví dụ qua Cloudflare Tunnel. |

Bật cả hai thì mỗi giao dịch vẫn chỉ được lưu một lần: ứng dụng nhận ra bản sao bằng mã tham chiếu của ngân hàng (hoặc thời gian, số tiền và nội dung khi ngân hàng không gửi mã).

## Cài đặt

1. Trên [my.sepay.vn](https://my.sepay.vn): thêm tài khoản ngân hàng chung (Tài khoản ngân hàng > Thêm).
2. Tạo khoá API: Cấu hình công ty > API Keys. Đặt vào tệp `.env` cạnh `docker-compose.family.yml`:
   ```
   SEPAY_API_KEY=...
   ```
3. (Tuỳ chọn) Webhook: Tích hợp webhook > Thêm webhook
   - URL: `https://<tên-miền-của-bạn>/api/sepay/webhook`
   - Kiểu chứng thực: **HMAC-SHA256**; chép secret vào `.env`: `SEPAY_WEBHOOK_SECRET=...`
   - Kiểu nội dung: `application/json`; bật tự gửi lại khi lỗi.
4. Khởi động lại: `docker compose -f docker-compose.family.yml up -d`.
5. Trong ứng dụng: tab **Ngân sách** > mục **Chung** > nhập số tài khoản chung và ngân hàng > **Lưu**. Bấm **Đồng bộ ngay** để kéo ngay các giao dịch 35 ngày gần nhất.

## Bảo mật

- Khoá API và secret chỉ nằm trong biến môi trường của máy chủ, không lưu trong cơ sở dữ liệu và không hiện trên giao diện.
- Webhook từ chối mọi yêu cầu không có chữ ký HMAC đúng, hoặc có dấu thời gian lệch quá 5 phút (chống gửi lại). Khi chưa đặt secret, địa chỉ webhook trả 404.
- Nếu muốn chặn thêm ở tường lửa, chỉ cho phép IP của SePay (danh sách tại https://developer.sepay.vn/vi/dia-chi-ip).
- Quỹ riêng của mỗi người không đi qua tài khoản chung nên ứng dụng không thấy.

## Biến môi trường

| Biến | Mặc định | Ý nghĩa |
|---|---|---|
| `LUBELOGGER_SEPAY_API_KEY` | trống (tắt) | Khoá API SePay v2 |
| `LUBELOGGER_SEPAY_WEBHOOK_SECRET` | trống (tắt) | Secret HMAC của webhook |
| `LUBELOGGER_SEPAY_SYNC_MINUTES` | 15 | Chu kỳ kéo qua API; 0 để tắt |
| `LUBELOGGER_SEPAY_SYNC_DAYS` | 35 | Lần đầu kéo lùi bao nhiêu ngày |
| `LUBELOGGER_SEPAY_API_URL` | `https://userapi.sepay.vn/v2` | Đổi sang `https://userapi-sandbox.sepay.vn/v2` để thử |
