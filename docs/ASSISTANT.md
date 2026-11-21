# Trợ lý AI (nút chat ở góc màn hình)

Nút tròn ở góc dưới bên phải, có trên mọi trang sau khi đăng nhập (cả **Tài chính gia đình** và **Bảo trì xe cộ**).
Bấm vào sẽ mở khung chat trượt từ bên phải; trên điện thoại khung chiếm cả màn hình. Thay cho tab **Trợ lý** cũ
(đường dẫn cũ `?tab=assistant` vẫn mở khung chat).

## Hỏi được gì

| Câu hỏi ví dụ | Trả lời |
|---|---|
| Tài khoản chung còn bao nhiêu? | số dư, tiền vào và ra từ đầu kỳ, giao dịch mới nhất |
| Tháng này / tháng trước chi bao nhiêu? | tổng vào, ra, chênh lệch, 3 mục chi lớn nhất, phần chưa phân loại, phần suy ra từ số dư |
| Đi chợ tháng này hết bao nhiêu? | chi của một mục ngân sách so với hạn mức (chỉ tính giao dịch đã gắn mục) |
| Sắp tới phải trả gì? | ngày lương, kỳ trả nợ từ nay đến đầu kỳ sau |
| 5 giao dịch gần nhất | các giao dịch mới nhất (tối đa 20) |
| Tìm giao dịch grab | giao dịch 90 ngày qua có chữ đó (không phân biệt dấu) |
| Thay nhớt Vision 12.350 km 120k | bản nháp ghi chép xe, bấm **Lưu** mới ghi |
| Ảnh (nút máy ảnh) | hóa đơn hoặc chuyển khoản thành bản nháp; nhãn thực phẩm được đọc và chấm điểm, bấm **Kiểm tra và lưu** để sửa số liệu trong tab Nhãn thực phẩm |

## Cách làm, theo thông lệ an toàn

- **AI chỉ chọn, code tính.** Câu hỏi được nhận diện bằng quy tắc từ khóa trước; câu nào không rõ thì AI ở nhà (qwen3)
  chọn **một** trong các tra cứu chỉ-đọc ở trên, theo JSON schema có danh sách cố định. Mọi con số do máy chủ tính từ
  dữ liệu đã lưu; AI không thấy giao dịch của gia đình và không thể bịa số. Mục ngân sách AI chọn phải nằm trong danh
  sách, số lượng bị giới hạn, từ khóa bị làm sạch.
- **Không ghi gì nếu chưa bấm Lưu.** Bản nháp xe và chuyển khoản chỉ được lưu bằng nút Lưu (web) hoặc
  `POST /api/v1/assistant/drafts` / `payments` (app); nhãn thực phẩm không tự lưu.
- **Chữ trong ảnh là dữ liệu.** Nội dung đọc từ ảnh chỉ điền vào bản nháp để người dùng kiểm tra, không bao giờ được
  dùng làm câu lệnh. Câu trả lời hiển thị bằng `text()`, không chèn HTML.
- **Lịch sử riêng từng người, 30 ngày.** Mỗi người chỉ thấy lịch sử của mình; tin cũ hơn 30 ngày tự xóa; có nút
  thùng rác để xóa hết. Ảnh không được lưu vào lịch sử, chỉ lưu dòng "[Ảnh]".
- **Giới hạn 20 tin mỗi phút cho mỗi người** (HTTP 429 + `Retry-After`), vì AI ở nhà chỉ xử lý từng yêu cầu một.
- **Chống gửi chéo trang.** Các lệnh gửi trên web cần header `X-Requested-With` (chỉ script của trang gửi được);
  API cần `Authorization: Bearer`.
- Không có AI (chưa đặt `LUBELOGGER_AI_URL`) thì vẫn trả lời được các câu theo quy tắc; ảnh cần AI.

## API v1 cho app Flutter

Xem bảng trong [IOS-API.md](IOS-API.md) và đặc tả `wwwroot/api/openapi-v1.json` (các schema `Assistant*`, `Ai*`).
Câu trả lời (`AssistantReply`) có `kind` (`answer`, `draft`, `payment`, `food`, `help`, `error`), `text`, `facts`
(cặp nhãn – giá trị đã định dạng), `suggestions` (nút gợi ý tiếp theo), `warnings`, và tùy loại `draft`, `payment` +
`matches`, `food`. Câu trả lời bằng tiếng Việt.

## Giới hạn hiện tại

- Chi theo mục ngân sách chỉ tính giao dịch đã gắn mục (nhập tay ở Đối soát); giao dịch SePay chưa được phân loại tự động.
- Chưa trả lời câu hỏi tự do ngoài các tra cứu trên (cố ý, để AI không tự đưa ra số liệu).
