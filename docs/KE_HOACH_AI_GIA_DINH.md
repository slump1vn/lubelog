# Kế hoạch: Phần mềm quản lý bảo trì xe gia đình (ô tô + xe máy) có AI

Nền tảng: fork **LubeLogger** (repo này — ASP.NET Core .NET 10, LiteDB/PostgreSQL, MVC + Razor, có sẵn REST API `Controllers/API`).
Chiến lược: **giữ lõi LubeLogger, mở rộng** thay vì viết lại — tận dụng sẵn ServiceRecord, ReminderRecord, OdometerRecord, GasRecord, Supply, PlanRecord, file đính kèm, đa người dùng, API.

---

## 1. Mục tiêu

| # | Mục tiêu | Đo lường |
|---|----------|----------|
| 1 | Quản lý toàn bộ xe trong gia đình (ô tô, xe máy) ở một nơi | Mỗi thành viên thấy xe được chia sẻ |
| 2 | Nhắc bảo dưỡng đúng hạn theo **km hoặc thời gian** (cái nào đến trước) | 0 lần quá hạn thay dầu |
| 3 | Nhập liệu **< 15 giây/lần** nhờ AI: chat, chụp hoá đơn, chụp ảnh chuyển khoản | ≥ 80% bản ghi tạo qua AI không phải sửa |
| 4 | Theo dõi chi phí theo xe / hạng mục / tháng (VNĐ) | Báo cáo tháng tự động |

## 2. Phạm vi nghiệp vụ

### 2.1 Loại xe
- Ô tô (xăng / dầu / hybrid / điện), xe máy (số, tay ga, côn tay, xe điện).
- Thêm trường `VehicleCategory` (Car/Motorbike) vào `Models/Vehicle` → quyết định bộ lịch bảo dưỡng mặc định.

### 2.2 Hạng mục bảo trì (template mặc định, người dùng chỉnh được)

| Hạng mục | Ô tô (mặc định) | Xe máy (mặc định) | Ghi chú |
|---|---|---|---|
| Thay dầu động cơ | 5.000–10.000 km / 6 tháng | 1.500–2.000 km / 3 tháng | Theo loại dầu (khoáng/bán tổng hợp/tổng hợp) |
| Lọc dầu | Mỗi 1–2 lần thay dầu | 2–3 lần thay dầu (nếu có) | Liên kết với lần thay dầu |
| Dầu hộp số / nhớt láp (tay ga) | 40.000–60.000 km | 6.000–8.000 km | |
| Nước làm mát | 40.000 km / 2 năm | 20.000 km / 2 năm (xe có két nước) | |
| Ắc quy | Kiểm tra 6 tháng, thay 2–3 năm | Thay 2 năm | Lưu ngày lắp, hãng, dung lượng, BH |
| Lốp | Đảo lốp 10.000 km, thay 40.000–50.000 km / 5 năm | Thay 15.000–20.000 km | Lưu vị trí lốp, DOT, độ sâu gai |
| Lọc gió động cơ / điều hoà | 20.000 km / 10.000 km | 8.000–12.000 km | |
| Má phanh, dầu phanh | 30.000 km / 2 năm | Kiểm tra 6.000 km | |
| Bugi | 40.000–100.000 km | 8.000–10.000 km | |
| Nhông xích (xe số) / dây curoa (tay ga) | — | 15.000–25.000 km | |
| Bảo dưỡng định kỳ toàn bộ | Gói cấp 5k/10k/20k/40k km | Gói 1k/4k/8k/12k km | Gói = nhiều hạng mục con |
| Đăng kiểm, bảo hiểm TNDS, phí đường bộ | Theo hạn | BH TNDS theo năm | Dùng TaxRecord + Reminder |

### 2.3 Thay đổi mô hình dữ liệu (tối thiểu)

```
MaintenanceTemplate      (Id, VehicleCategory, Name, IntervalKm, IntervalMonths, LinkedItems[])
ServiceRecord  + ItemType (enum: OilChange, OilFilter, Coolant, Battery, Tire, FullService, ...)
               + Payment   (Method, BankName, TransactionRef, PaidAt, PayerUserId)
               + Source    (Manual | Chat | ReceiptOCR | TransferOCR)
               + AiConfidence, AiRawJson
BatteryInfo / TireInfo   (lưu dạng ExtraField hoặc bảng riêng: hãng, size, DOT, vị trí, ngày lắp)
AiIngestionJob           (Id, UserId, Type, FileId, Status, ExtractedJson, CreatedRecordIds[])
```
Reminder tự sinh: khi lưu ServiceRecord có `ItemType` → tạo/ cập nhật ReminderRecord kế tiếp theo template (dùng logic sẵn trong `Logic/` + `ReminderConfig`).

## 3. Tính năng AI

### 3.1 Kiến trúc chung

```
[Web/PWA/Zalo/Telegram]
      │ text / ảnh
      ▼
AiController (/api/ai/*) ──► AiIngestionService
      │                         ├─ Vision LLM (đọc ảnh) ─┐
      │                         ├─ LLM + tool calling ───┤→ JSON có schema
      │                         └─ Rule/validator (VNĐ, ngày, km, biển số)
      ▼
Màn hình "Xác nhận" (draft) ──► người dùng sửa/OK ──► ServiceRecord/GasRecord/... + file đính kèm
```
- **Luôn có bước xác nhận** (human-in-the-loop); bản ghi AI tạo ở trạng thái *Draft* cho tới khi người dùng chấp nhận.
- Provider trừu tượng qua `IAiProvider` (Claude API làm mặc định; có thể thay bằng model chạy local như Ollama cho người muốn self-host riêng tư).
- Dùng **tool calling / structured output**: LLM chỉ được gọi các tool `create_service_record`, `create_fuel_record`, `update_odometer`, `query_history`, `get_due_reminders` — không ghi DB trực tiếp.

### 3.2 Nhập liệu qua chat
Ví dụ:
- *"Hôm nay thay nhớt Vision ở 12.350 km, hết 120k, có thay lọc"* → ServiceRecord (OilChange + OilFilter), cập nhật odo, lên lịch nhắc 14.350 km.
- *"Đổ xăng CX5 800 nghìn, 36 lít, odo 45200"* → GasRecord.
- *"Bao giờ phải thay ắc quy xe vợ?"*, *"Tháng này cả nhà tiêu bao nhiêu cho xe?"* → truy vấn.

Kỹ thuật: system prompt chứa danh sách xe của user (tên, biệt danh, biển số, odo hiện tại), danh mục hạng mục; chuẩn hoá tiếng Việt ("120k", "1tr2", "1,2 triệu", "hôm qua"). Nếu thiếu thông tin (xe nào?) → hỏi lại 1 câu.

### 3.3 Chụp hoá đơn (garage, head Honda, cửa hàng lốp...)
1. Upload ảnh/PDF → lưu bằng `FilesController` có sẵn.
2. Vision LLM trích xuất: tên garage, ngày, biển số, km, **danh sách dòng** (tên phụ tùng/công, SL, đơn giá, thành tiền), VAT, tổng.
3. Map từng dòng → `ItemType` (vd "Nhớt Motul 300V 1L" → OilChange; "Lọc nhớt" → OilFilter; "Lốp Michelin 205/55R16" → Tire).
4. Đối chiếu tổng = Σ dòng; lệch → cảnh báo.
5. Hoá đơn điện tử (XML/QR tra cứu) → parse trực tiếp, không cần OCR.

### 3.4 Chụp ảnh chuyển khoản (app ngân hàng / MoMo / ZaloPay)
- Trích xuất: số tiền, thời gian, ngân hàng, mã giao dịch, người nhận, nội dung CK.
- **Ghép cặp** với hoá đơn/bản ghi đang mở: cùng số tiền ± 1%, cách nhau ≤ 3 ngày, người nhận trùng tên garage → gắn `Payment` vào ServiceRecord, đánh dấu *Đã thanh toán*.
- Không khớp → tạo bản ghi chi phí "chưa phân loại" + hỏi người dùng là cho xe nào/việc gì.
- Chống trùng: khoá duy nhất theo `TransactionRef`.

### 3.5 AI bổ trợ (giai đoạn sau)
- Gợi ý lịch bảo dưỡng theo mẫu xe + quãng đường TB/tháng (dự đoán ngày đạt km).
- Phát hiện bất thường: tiêu hao nhiên liệu tăng, giá phụ tùng cao hơn mức thường.
- Tóm tắt tháng gửi qua email/Zalo.

## 4. Bảo mật & riêng tư
- Ảnh CK chứa số tài khoản → che (mask) trước khi lưu, chỉ giữ 4 số cuối; không gửi lại ảnh gốc cho LLM sau khi trích xuất.
- API key AI lưu trong cấu hình server (`appsettings`/env), không lộ ra client.
- Tuỳ chọn tắt AI hoàn toàn hoặc dùng model local.
- Phân quyền theo hộ gia đình: dùng cơ chế chia sẻ xe/`UserAccess` sẵn có.

## 5. Lộ trình

| Giai đoạn | Thời gian | Nội dung | Kết quả |
|---|---|---|---|
| **0. Chuẩn bị** | 1 tuần | Fork, chạy Docker, Việt hoá (LubeLogger có sẵn translation), VNĐ, định dạng ngày dd/MM/yyyy | Bản chạy được tiếng Việt |
| **1. Lõi bảo trì** | 2–3 tuần | `VehicleCategory`, `ItemType`, MaintenanceTemplate ô tô/xe máy, reminder tự sinh, hồ sơ ắc quy & lốp, gói bảo dưỡng toàn bộ | Nhắc lịch chuẩn cho cả 2 loại xe |
| **2. AI chat** | 2 tuần | `IAiProvider`, `/api/ai/chat`, tool calling, màn hình xác nhận, widget chat trong web | Nhập bằng câu tiếng Việt |
| **3. OCR hoá đơn** | 2 tuần | Upload ảnh → trích xuất → map dòng → draft | Chụp hoá đơn tạo bản ghi |
| **4. Ảnh chuyển khoản** | 1–2 tuần | Trích xuất CK, ghép cặp, chống trùng, mask STK | Theo dõi đã/chưa thanh toán |
| **5. Mobile & kênh** | 2 tuần | PWA (camera), bot Telegram/Zalo OA gọi cùng API | Nhập từ điện thoại |
| **6. Hoàn thiện** | liên tục | Báo cáo chi phí gia đình, dự đoán, tối ưu prompt, bộ test ảnh mẫu | |

## 6. Kiểm thử
- Bộ dữ liệu mẫu: ≥ 50 hoá đơn thật (ẩn thông tin) + 30 ảnh CK của các ngân hàng phổ biến (VCB, TCB, MB, ACB, MoMo...).
- Chỉ số: độ chính xác tổng tiền, ngày, km, phân loại hạng mục; tỷ lệ phải sửa tay.
- Unit test cho parser tiền tệ/ngày tiếng Việt và logic ghép cặp CK.

## 7. Quyết định đã chốt

| Câu hỏi | Quyết định |
|---|---|
| Kênh nhập chính | **Zalo** (Zalo Official Account + webhook) |
| AI | **Chạy tại nhà** (local, không gửi dữ liệu ra ngoài) |
| CSDL | **LiteDB** (1 file, đơn giản, đủ cho gia đình) |
| Phạm vi chi phí | **Quản lý mọi chi phí xe**, không chỉ bảo trì |

### 7.1 Kênh Zalo
- Tạo **Zalo Official Account** (OA) cho gia đình, đăng ký ứng dụng tại developers.zalo.me, bật webhook sự kiện `user_send_text`, `user_send_image`, `user_send_file`.
- Thêm `ZaloController` (`/api/zalo/webhook`):
  - Xác thực chữ ký webhook (`X-ZEvent-Signature` = SHA256(appId + body + timestamp + OA secret key)).
  - Tải ảnh từ URL Zalo trả về → lưu qua `FilesController` → đưa vào `AiIngestionService`.
  - Trả lời bằng OA API "gửi tin tư vấn" (trong 7 ngày kể từ tin cuối của người dùng — đủ cho luồng hỏi–đáp).
- **Liên kết tài khoản:** mỗi thành viên gửi mã liên kết (vd `/lienket 123456`, mã lấy từ trang cá nhân trên web) → lưu `ZaloUserId ↔ UserId`. Người lạ chưa liên kết bị bỏ qua.
- **Xác nhận trong chat:** bot gửi tóm tắt bản nháp kèm lựa chọn trả lời nhanh: `1 = Lưu`, `2 = Sửa`, `3 = Huỷ`. Sửa bằng câu tự nhiên ("sửa số km thành 12400").
- **Nhắc lịch qua Zalo:** job hằng ngày (dùng cơ chế reminder sẵn có) gửi tin "Sắp đến hạn thay dầu xe Vision (còn 200 km)". Ngoài khung 7 ngày → dùng ZNS hoặc email fallback.
- Server cần **HTTPS công khai** để Zalo gọi webhook: dùng Cloudflare Tunnel (miễn phí, không cần mở cổng router) trỏ về máy chủ tại nhà.
- Access token OA hết hạn sau ~25 giờ → job tự refresh bằng refresh token, lưu trong LiteDB.

### 7.2 AI chạy tại nhà
- **Runtime:** Ollama (Docker), gọi qua HTTP từ `OllamaAiProvider : IAiProvider`.
- **Model đề xuất** (thử nghiệm, chọn cái tốt nhất với bộ ảnh mẫu):
  - Hiểu ảnh hoá đơn/CK: model vision đa ngôn ngữ cỡ 7–8B (vd họ Qwen-VL hoặc Gemma 3 có vision).
  - Chat + tool calling: model 7–14B hỗ trợ tiếng Việt và JSON tốt.
- **Phần cứng tối thiểu:** GPU 8 GB VRAM (vd RTX 3060) hoặc Mac Apple Silicon 16 GB; chạy CPU được nhưng chậm (30–90 giây/ảnh) — chấp nhận được vì Zalo là bất đồng bộ (bot gửi "Đang đọc hoá đơn…" trước).
- **Tăng độ chính xác khi model nhỏ:**
  1. Tiền xử lý ảnh (xoay, cắt, tăng tương phản) bằng ImageSharp.
  2. Tuỳ chọn OCR chuyên dụng trước (PaddleOCR + VietOCR trong container Python) → đưa text cho LLM, LLM chỉ còn việc cấu trúc hoá.
  3. Ép đầu ra JSON theo schema (`format: json` của Ollama) + validator phía .NET; sai thì hỏi lại người dùng thay vì đoán.
  4. Bộ luật regex cho ảnh CK của các ngân hàng phổ biến (bố cục cố định) — nhanh và chính xác hơn LLM.
- Hàng đợi xử lý 1 job/lần (`BackgroundService` + `Channel<T>`) để không quá tải GPU.

### 7.3 LiteDB
- Giữ mặc định của LubeLogger, không cần cấu hình Postgres.
- Bảng mới: `ExpenseRecord`, `AiIngestionJob`, `ZaloLink`, `ZaloToken`, `MaintenanceTemplate`.
- LiteDB khoá ghi theo file → mọi ghi từ webhook/AI worker đi qua cùng repository có sẵn, tránh mở nhiều kết nối.
- **Sao lưu:** job đêm copy file `.db` + thư mục ảnh (LubeLogger có sẵn tính năng backup zip) sang ổ khác / Google Drive.

### 7.4 Quản lý mọi chi phí xe

| Loại chi phí | Lưu vào | Ghi chú |
|---|---|---|
| Bảo dưỡng, sửa chữa | `ServiceRecord` / `CollisionRecord` (sửa chữa) | như mục 2 |
| Xăng, sạc điện | `GasRecord` (có sẵn) | tính L/100km, đ/km |
| Phí đường bộ, đăng kiểm, bảo hiểm (TNDS, thân vỏ) | `TaxRecord` (có sẵn, có lặp lại) | tự nhắc gia hạn |
| Phụ kiện, nâng cấp | `UpgradeRecord` (có sẵn) | |
| **Gửi xe, phí BOT/ETC, rửa xe, phạt nguội, thuê lái, vé tháng, khác** | **`ExpenseRecord` mới** | `Category`, `Amount`, `Date`, `VehicleId`, `Payment`, `Note`, `Source` |

- Danh mục chi phí người dùng tự thêm/sửa; AI phân loại tự động ("rửa xe 50k" → Rửa xe; "gửi xe tháng 11 300k" → Gửi xe, lặp hằng tháng).
- Ảnh CK không ghép được với hoá đơn nào → mặc định tạo `ExpenseRecord` và hỏi loại chi phí.
- **Báo cáo:** tổng chi theo xe / loại / tháng / thành viên, chi phí trên mỗi km, so sánh cùng kỳ; lệnh Zalo `/baocao` hoặc "tháng này tiêu bao nhiêu" trả về tóm tắt; báo cáo tự động ngày 1 hằng tháng.

## 8. Lộ trình (cập nhật)

| Giai đoạn | Thời gian | Nội dung |
|---|---|---|
| 0 | 1 tuần | Fork, Docker Compose (LubeLogger + Ollama + Cloudflare Tunnel), Việt hoá, VNĐ |
| 1 | 2–3 tuần | Lõi bảo trì ô tô/xe máy, template, reminder tự sinh, ắc quy/lốp |
| 2 | 1–2 tuần | `ExpenseRecord` + danh mục + báo cáo chi phí tổng hợp |
| 3 | 2 tuần | Zalo OA: webhook, liên kết tài khoản, gửi nhắc lịch, xác nhận 1/2/3 |
| 4 | 2 tuần | AI local: chat → bản nháp qua Ollama, tool calling, hàng đợi |
| 5 | 2–3 tuần | Ảnh hoá đơn + ảnh CK (OCR + LLM + luật ngân hàng), ghép cặp, mask STK |
| 6 | liên tục | Đo độ chính xác với bộ ảnh mẫu, tinh chỉnh prompt/model, backup |

**Bước tiếp theo:** bắt đầu giai đoạn 0–1 trong repo này.
