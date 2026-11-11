# Triển khai lên máy ảo Proxmox

Script `create-vm.sh` tạo một máy ảo Ubuntu 24.04 trên Proxmox, cài Docker, tải mã nguồn và chạy
`docker-compose.family.yml`. **Script chưa được chạy thử trên Proxmox thật** — hãy đọc qua trước khi dùng.

## Yêu cầu

- Proxmox VE 8 trở lên, máy chủ có Internet.
- Kho `local` bật loại nội dung **Snippets** (Datacenter > Storage > local > Edit > Content).
- Máy ảo mặc định: 2 nhân, 4 GB RAM, đĩa 32 GB. Muốn chạy AI tại nhà (Ollama) cần nhiều hơn:
  tối thiểu 4 nhân, 12-16 GB RAM, đĩa 60 GB; không có GPU thì mỗi câu mất vài chục giây.

## Các bước

1. Mở Shell của node Proxmox (giao diện web `https://192.168.100.200:8006` > chọn node > Shell).
2. Tải script và chạy:

   ```bash
   wget -O create-vm.sh https://raw.githubusercontent.com/slump1vn/lubelog/ccr-4bc84112-86lk4w/deploy/proxmox/create-vm.sh
   REPO_URL=https://github.com/slump1vn/lubelog.git BRANCH=ccr-4bc84112-86lk4w \
   CI_PASSWORD='dat-mat-khau-o-day' bash create-vm.sh
   ```

   Nếu kho mã ở chế độ riêng tư, lệnh `wget` và `git clone` cần token GitHub (quyền chỉ đọc):
   dán nội dung script bằng tay và dùng `REPO_URL='https://<token>@github.com/slump1vn/lubelog.git'`.
3. Đợi 5-10 phút, lấy IP máy ảo bằng lệnh script in ra, rồi mở `http://<ip-may-ao>:8080`.
4. Vào Cài đặt > bật **Đăng nhập** và tạo tài khoản trước khi cho cả nhà dùng.

## Sau khi cài

- Cập nhật phiên bản mới: `cd /opt/lubelog && git pull && docker compose -f docker-compose.family.yml up -d --build`
- Bật AI tại nhà: xem ghi chú trong `docker-compose.family.yml` (mục `LUBELOGGER_AI_URL`).
- Nên đặt IP cố định cho máy ảo (biến `IPCONFIG` khi tạo, hoặc giữ chỗ DHCP trên router).
- Sao lưu: dùng tính năng Backup của Proxmox cho cả máy ảo, hoặc mục Sao lưu trong Cài đặt của phần mềm.
