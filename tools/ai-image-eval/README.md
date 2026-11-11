# Đo độ chính xác đọc ảnh hoá đơn / chuyển khoản

`images/` là ảnh mẫu tự tạo (không phải chứng từ thật): phiếu garage in, phiếu viết tay nghiêng,
hoá đơn xăng mờ tối và ảnh chụp màn hình chuyển khoản. `expected.json` ghi giá trị đúng.

```
python eval.py --url http://192.168.2.135:9292 --api openai      # llama.cpp
python eval.py --url http://localhost:11434 --api ollama          # Ollama
python eval.py --url ... --images D:/anh-that                     # ảnh của bạn, chỉ in ra kết quả
```

Lời nhắc được đọc thẳng từ `Logic/AiLogic.cs`, nên sửa lời nhắc ở đó rồi chạy lại là đo được ngay.
