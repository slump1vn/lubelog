#!/usr/bin/env bash
# Tạo một máy ảo Ubuntu trên Proxmox và tự cài phần mềm quản lý xe gia đình.
#
# Chạy trên CHÍNH MÁY CHỦ PROXMOX (Shell của node trong giao diện web, hoặc ssh root@<ip-proxmox>):
#
#   REPO_URL=https://github.com/slump1vn/lubelog.git BRANCH=ccr-4bc84112-86lk4w \
#   CI_PASSWORD='mat-khau-dang-nhap-may-ao' bash create-vm.sh
#
# Biến tuỳ chọn (giá trị mặc định trong ngoặc):
#   VMID (tự lấy số trống kế tiếp)   VM_NAME (lubelog)      STORAGE (local-lvm)
#   BRIDGE (vmbr0)                   MEMORY (4096 MB)       CORES (2)
#   DISK_SIZE (32G)                  IPCONFIG (ip=dhcp)     CI_USER (lubelog)
#   SSH_KEY_FILE (tệp khoá công khai, ví dụ /root/.ssh/authorized_keys)
#   SNIPPET_STORAGE (local)          WITH_AI (0; đặt 1 để chạy kèm Ollama)
#
# Ví dụ IP tĩnh: IPCONFIG='ip=192.168.100.210/24,gw=192.168.100.1'
# Kho mã riêng tư: REPO_URL='https://<token>@github.com/slump1vn/lubelog.git'
set -euo pipefail

REPO_URL="${REPO_URL:?Cần đặt REPO_URL (địa chỉ git của kho mã)}"
BRANCH="${BRANCH:-master}"
VMID="${VMID:-$(pvesh get /cluster/nextid)}"
VM_NAME="${VM_NAME:-lubelog}"
STORAGE="${STORAGE:-local-lvm}"
SNIPPET_STORAGE="${SNIPPET_STORAGE:-local}"
BRIDGE="${BRIDGE:-vmbr0}"
MEMORY="${MEMORY:-4096}"
CORES="${CORES:-2}"
DISK_SIZE="${DISK_SIZE:-32G}"
IPCONFIG="${IPCONFIG:-ip=dhcp}"
CI_USER="${CI_USER:-lubelog}"
CI_PASSWORD="${CI_PASSWORD:-}"
SSH_KEY_FILE="${SSH_KEY_FILE:-}"
WITH_AI="${WITH_AI:-0}"
IMAGE_URL="${IMAGE_URL:-https://cloud-images.ubuntu.com/noble/current/noble-server-cloudimg-amd64.img}"
IMAGE_FILE="/var/lib/vz/template/iso/$(basename "$IMAGE_URL")"

command -v qm >/dev/null || { echo "Không thấy lệnh 'qm': script này phải chạy trên máy chủ Proxmox."; exit 1; }
if [ -z "$CI_PASSWORD" ] && [ -z "$SSH_KEY_FILE" ]; then
  echo "Cần đặt CI_PASSWORD hoặc SSH_KEY_FILE để đăng nhập được vào máy ảo."; exit 1
fi
if qm status "$VMID" >/dev/null 2>&1; then
  echo "Máy ảo $VMID đã tồn tại. Đặt VMID khác."; exit 1
fi
SNIPPET_DIR="$(pvesm path "${SNIPPET_STORAGE}:snippets/x" 2>/dev/null | xargs -r dirname || true)"
if [ -z "$SNIPPET_DIR" ] || ! pvesm status --content snippets 2>/dev/null | awk 'NR>1{print $1}' | grep -qx "$SNIPPET_STORAGE"; then
  echo "Kho '$SNIPPET_STORAGE' chưa bật loại nội dung 'Snippets'."
  echo "Bật tại: Datacenter > Storage > $SNIPPET_STORAGE > Edit > Content > chọn thêm Snippets, rồi chạy lại."
  exit 1
fi

COMPOSE_ARGS="-f docker-compose.family.yml"
[ "$WITH_AI" = "1" ] && COMPOSE_ARGS="$COMPOSE_ARGS --profile ai"

mkdir -p "$SNIPPET_DIR"
SNIPPET_FILE="$SNIPPET_DIR/lubelog-${VMID}-vendor.yaml"
cat > "$SNIPPET_FILE" <<CLOUDINIT
#cloud-config
package_update: true
packages:
  - qemu-guest-agent
  - git
  - ca-certificates
  - curl
runcmd:
  - systemctl enable --now qemu-guest-agent
  - curl -fsSL https://get.docker.com | sh
  - git clone --branch "${BRANCH}" --depth 1 "${REPO_URL}" /opt/lubelog
  - cd /opt/lubelog && docker compose ${COMPOSE_ARGS} up -d --build
CLOUDINIT
chmod 600 "$SNIPPET_FILE"

if [ ! -f "$IMAGE_FILE" ]; then
  echo "Tải ảnh Ubuntu 24.04..."
  mkdir -p "$(dirname "$IMAGE_FILE")"
  wget -q --show-progress -O "$IMAGE_FILE.tmp" "$IMAGE_URL"
  mv "$IMAGE_FILE.tmp" "$IMAGE_FILE"
fi

echo "Tạo máy ảo $VMID ($VM_NAME)..."
qm create "$VMID" --name "$VM_NAME" --ostype l26 --memory "$MEMORY" --cores "$CORES" --cpu host \
  --net0 "virtio,bridge=${BRIDGE}" --scsihw virtio-scsi-pci --agent enabled=1 --onboot 1 \
  --serial0 socket --vga serial0
qm set "$VMID" --scsi0 "${STORAGE}:0,import-from=${IMAGE_FILE}"
qm set "$VMID" --ide2 "${STORAGE}:cloudinit" --boot order=scsi0
qm resize "$VMID" scsi0 "$DISK_SIZE"
qm set "$VMID" --ciuser "$CI_USER" --ipconfig0 "$IPCONFIG" --cicustom "vendor=${SNIPPET_STORAGE}:snippets/$(basename "$SNIPPET_FILE")"
[ -n "$CI_PASSWORD" ] && qm set "$VMID" --cipassword "$CI_PASSWORD" >/dev/null
[ -n "$SSH_KEY_FILE" ] && qm set "$VMID" --sshkeys "$SSH_KEY_FILE"
qm start "$VMID"

echo
echo "Máy ảo $VMID đã khởi động. Lần đầu mất khoảng 5-10 phút để cài Docker và build phần mềm."
echo "Xem địa chỉ IP (sau 1-2 phút):   qm guest cmd $VMID network-get-interfaces | grep '\"ip-address\"'"
echo "Theo dõi tiến trình cài đặt:      qm terminal $VMID   (thoát bằng Ctrl+O), rồi: tail -f /var/log/cloud-init-output.log"
echo "Khi xong, mở trình duyệt:         http://<ip-may-ao>:8080"
