# Nhãn thực phẩm (Food labels)

Tab **Nhãn thực phẩm** trong phần Tài chính gia đình: chụp nhãn sản phẩm, app chỉ ra chất béo, chất béo bão hòa,
đường, muối, phụ gia (chất bảo quản, phẩm màu, chất tạo ngọt...) và chất gây dị ứng, kèm nguồn tham chiếu.

## Cách hoạt động

1. Chụp 1 đến 3 ảnh của cùng một sản phẩm (bảng dinh dưỡng, thành phần, mặt trước). Trình duyệt thu nhỏ ảnh về
   1600 px rồi gửi lên. Máy chủ **không lưu ảnh**: ảnh chỉ nằm trong bộ nhớ trong lúc AI đọc.
2. AI ở nhà (`LUBELOGGER_AI_VISION_MODEL`, ví dụ `qwen3-vl`) chỉ **chép lại chữ** trên nhãn thành JSON
   (`FoodLogic.BuildPrompt`). AI không đánh giá gì.
3. Người dùng **kiểm tra và sửa** số liệu; ô AI không chắc được tô vàng. Có thể nhập tay khi AI đang tắt.
4. `FoodLabelRules` (code, có test) quy đổi về 100 g/100 ml, đổi natri sang muối (muối = natri × 2,5), chấm đèn,
   tra phụ gia và dị ứng. Cùng một nhãn luôn cho cùng một kết quả.

## Nguồn tham chiếu (`Logic/FoodReference.cs`)

| Nội dung | Nguồn |
|---|---|
| Đèn xanh/vàng/đỏ trên 100 g (đồ uống: 100 ml), quy tắc khẩu phần lớn (> 30% lượng tham chiếu) | UK FSA / DHSC, Front of pack nutrition labelling guidance |
| Giới hạn mỗi ngày: béo ≤ 30% năng lượng, béo bão hòa < 10%, béo chuyển hóa < 1%, đường tự do < 10% (50 g), muối < 5 g | WHO (2023, 2015, 2012) |
| Mã phụ gia và nhóm chức năng | Codex CXG 36-1989 (INS) |
| Ghi chú phụ gia: 6 phẩm màu "Southampton", E171 bị cấm ở EU, aspartame (IARC 2B, phenylalanin), nitrit, phosphat, glutamat, polyol, sulfit | EU 1333/2008, EU 2022/63, EU 1169/2011, EFSA, IARC, JECFA |
| 14 nhóm chất gây dị ứng | EU 1169/2011 Phụ lục II (bao gồm danh sách Codex) |
| Chất tạo ngọt không đường, caffeine, benzoat + vitamin C, dầu hydro hóa một phần | WHO 2023, EFSA 2015, US FDA, WHO REPLACE |

Phụ gia được phép và không có ghi chú chỉ hiện tên và công dụng, không bị tô đỏ. Khi sửa bảng này, tăng
`FoodReference.Version`; mỗi lần quét lưu lại phiên bản đã dùng.

Chưa làm: đối chiếu danh mục phụ gia được phép của Việt Nam (Thông tư 24/2019/TT-BYT) và tra mã vạch qua
Open Food Facts.

## Quyền riêng tư

- Lịch sử quét dùng chung cho cả nhà (ai quét, lúc nào, có thể gắn vào một khoản chi của tài khoản chung).
- **Hồ sơ dị ứng** là dữ liệu sức khỏe (dữ liệu cá nhân nhạy cảm theo Nghị định 13/2023/NĐ-CP): không bắt buộc,
  mỗi người tự bật, chỉ chính người đó xem được, xóa được bất cứ lúc nào (lưu hồ sơ trống cũng là xóa). Hồ sơ
  **không bao giờ gửi cho AI** và không lưu vào lần quét; dấu "có trong hồ sơ của bạn" được tính lúc xem.
- Kết quả luôn kèm dòng miễn trừ: đây là thông tin dinh dưỡng chung, không phải lời khuyên y tế.

## API cho app iPhone

Xem [IOS-API.md](IOS-API.md): `POST /api/v1/food/read`, `POST /api/v1/food/analyze`, `GET /api/v1/food/scans`.
