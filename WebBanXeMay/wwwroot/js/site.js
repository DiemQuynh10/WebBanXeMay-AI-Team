// ===== JS nền dùng chung cho Web Bán Xe Máy =====

// 💰 Định dạng tiền tệ VND
window.formatCurrency = function (n) {
    try {
        return n.toLocaleString('vi-VN', { style: 'currency', currency: 'VND' });
    } catch {
        return `${n} ₫`;
    }
};

// 📱 Toggle menu mobile
window.Nav = (function () {
    const el = () => document.getElementById('mobile-menu');

    function toggle() {
        const m = el();
        if (!m) return;
        m.classList.toggle('hidden');
    }

    return { toggle };
})();

// 🧭 Auto-hide header khi cuộn (tuỳ chọn, có thể bỏ)
(function () {
    const header = document.querySelector('header');
    if (!header) return;

    let lastScroll = 0;
    window.addEventListener('scroll', () => {
        const current = window.scrollY;
        if (current > lastScroll && current > 80) {
            // cuộn xuống -> ẩn header
            header.style.transform = 'translateY(-100%)';
        } else {
            // cuộn lên -> hiện lại
            header.style.transform = 'translateY(0)';
        }
        lastScroll = current;
    });
})();

// 🛒 Cập nhật nhanh số lượng trong giỏ hàng ở header (nếu có badge)
window.Cart = window.Cart || {};
window.Cart.refreshBadge = function (count) {
    const badge = document.getElementById('cart-badge');
    if (!badge) return;
    badge.textContent = count ?? badge.textContent;
};
