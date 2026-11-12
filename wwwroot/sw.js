// Service worker cho bản cài trên điện thoại (PWA / app Android).
// Cố ý KHÔNG lưu trang hay dữ liệu tài chính vào bộ nhớ đệm: mọi trang luôn lấy mới từ máy chủ.
// Chỉ lưu sẵn trang "mất mạng" và biểu tượng, để khi không có mạng người dùng thấy thông báo dễ hiểu
// thay vì trang lỗi của trình duyệt.
const CACHE = 'soi-tho-offline-v1';
const OFFLINE_URL = '/offline.html';
const PRECACHE = [OFFLINE_URL, '/defaults/app_icon_192.png'];

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(CACHE).then((cache) => cache.addAll(PRECACHE)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  if (req.method !== 'GET' || req.mode !== 'navigate') {
    return; // để trình duyệt xử lý như bình thường
  }
  event.respondWith(fetch(req).catch(() => caches.match(OFFLINE_URL)));
});
