// Hàm helper để lấy AntiForgeryToken
// (Cần đảm bảo @Html.AntiforgeryToken() được gọi trong _Layout)
function getToken() {
    const token = document.querySelector('input[name="__RequestVerificationToken"]');
    return token ? token.value : '';
}

// Hàm helper để format tiền
const formatVND = n => (Number(n) || 0).toLocaleString('vi-VN') + 'đ';

// Tạo một đối tượng 'Cart' toàn cục để quản lý
window.Cart = {
    /**
     * Cập nhật badge giỏ hàng (số lượng, tổng tiền trên header)
     */
    refreshBadge: async function () {
        try {
            const res = await fetch('/Cart/Count', {
                method: 'GET',
                headers: { 'Accept': 'application/json' }
            });
            if (!res.ok) return;

            const data = await res.json();

            // Cập nhật các element trên header (ví dụ)
            const elCount = document.getElementById('cart-badge-count');
            const elTotal = document.getElementById('cart-badge-total');

            if (elCount) elCount.textContent = data.count || 0;
            if (elTotal) elTotal.textContent = formatVND(data.total || 0);

        } catch (error) {
            console.error('Failed to refresh cart badge:', error);
        }
    },

    /**
     * Thêm sản phẩm vào giỏ (dùng cho trang chi tiết sản phẩm)
     * @param {number} maSP - Mã sản phẩm
     * @param {number} quantity - Số lượng
     * @param {function} onSuccess - Callback khi thành công
     */
    add: async function (maSP, quantity = 1, onSuccess) {
        if (!maSP || quantity < 1) return;

        try {
            const res = await fetch('/Cart/AddJson', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json',
                    'RequestVerificationToken': getToken() // <-- Rất quan trọng
                },
                body: JSON.stringify({ maSP: maSP, quantity: quantity })
            });

            if (res.status === 401) { // Chưa đăng nhập
                alert('Vui lòng đăng nhập để thêm sản phẩm.');
                // (Tùy chọn) Chuyển hướng sang trang đăng nhập
                // window.location.href = '/Identity/Account/Login?ReturnUrl=' + window.location.pathname;
                return;
            }

            const data = await res.json();

            if (res.ok && data.ok) {
                // Thành công!
                alert('Đã thêm vào giỏ hàng!'); // (Nên dùng toast/popup đẹp hơn)
                this.refreshBadge(); // Cập nhật lại badge
                if (onSuccess) onSuccess(data); // Gọi callback (nếu có)
            } else {
                // Lỗi do server (hết hàng, v.v.)
                alert(data.message || 'Thêm thất bại. Vui lòng thử lại.');
            }

        } catch (error) {
            console.error('Failed to add to cart:', error);
            alert('Có lỗi xảy ra. Vui lòng thử lại.');
        }
    }
};

// Tải badge ngay khi trang được load
document.addEventListener('DOMContentLoaded', () => {
    window.Cart.refreshBadge();
});