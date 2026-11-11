# Kết quả đọc ảnh với máy chủ llama.cpp thật (11/11/2026)

Máy chủ: llama-swap + llama.cpp b9152 tại 192.168.2.135:9292, ctx 4096.

| Mô hình | Kết quả | Thời gian |
|---|---|---|
| qwen3-vl (Qwen3VL-8B-Instruct Q4_K_M + mmproj F16) | 27/28 (96%) trước khi sửa, 28/28 (100%) sau khi sửa | 3,7–6,8 giây/ảnh |
| gemma4 (gemma-4-12b-it Q4_K_M) | 0/4 ảnh, lỗi 500 "image input is not supported" | — |

## qwen3-vl theo từng ảnh
- Phiếu garage in: 7/7. Đúng tên gara, ngày, tổng 1.188.000 (đã gồm VAT), km 45.320, biển số, 4 dòng.
- Phiếu viết tay nghiêng: 6/7. Đọc đúng "120k" thành 120000. Sai: lấy "678.90" trong biển số 59X2-678.90 làm số km.
- Hoá đơn xăng mờ tối nghiêng: 7/7.
- Ảnh chuyển khoản: 7/7. Đúng mã GD, người nhận, nội dung; không lộ số tài khoản.
- Máy chủ chấp nhận `response_format` kiểu json_schema.

## Đã sửa
- Lời nhắc: chỉ lấy số km khi phiếu ghi rõ "Km"/"số km", không lấy số trong biển số.
- Kiểm tra sau: bỏ số km có phần lẻ, nhỏ hơn 10, hoặc nằm trong dãy số của biển số.

## Cấu hình đề xuất
```
LUBELOGGER_AI_URL=http://192.168.2.135:9292
LUBELOGGER_AI_API=openai
LUBELOGGER_AI_KEY=<khoá llama-swap>
LUBELOGGER_AI_VISION_MODEL=qwen3-vl
LUBELOGGER_AI_MODEL=qwen3
```
gemma4 muốn đọc ảnh phải thêm `--mmproj` vào lệnh trong llama-swap; mô hình này còn tự bật chế độ suy nghĩ, nên tắt nếu dùng.

## Chưa làm
- Chưa build .NET (máy chưa có SDK) và chưa thử với ảnh thật của gia đình.
