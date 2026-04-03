import { mockProducts } from './mock-products.js';
import '../js/cart.js'; // khởi tạo Cart + badge

const state = {
    page: 1,
    pageSize: 6,
    brand: '',
    priceRange: '',
    sortBy: '',
    searchKey: ''
};

function filterProducts(list) {
    let rs = [...list];

    if (state.brand) rs = rs.filter(p => p.brand === state.brand);

    if (state.priceRange) {
        const [min, max] = state.priceRange.split('-').map(Number);
        rs = rs.filter(p => p.price >= min && p.price <= max);
    }

    if (state.searchKey) {
        const k = state.searchKey.toLowerCase();
        rs = rs.filter(p => p.name.toLowerCase().includes(k));
    }

    switch (state.sortBy) {
        case 'price-asc': rs.sort((a, b) => a.price - b.price); break;
        case 'price-desc': rs.sort((a, b) => b.price - a.price); break;
        case 'name-asc': rs.sort((a, b) => a.name.localeCompare(b.name)); break;
    }

    return rs;
}

function paginate(list) {
    const start = (state.page - 1) * state.pageSize;
    return list.slice(start, start + state.pageSize);
}

function renderGrid() {
    const grid = document.getElementById('product-grid');
    const pageInfo = document.getElementById('page-info');

    const filtered = filterProducts(mockProducts);
    const total = filtered.length;
    const totalPages = Math.max(1, Math.ceil(total / state.pageSize));
    state.page = Math.min(state.page, totalPages);

    const pageData = paginate(filtered);

    grid.innerHTML = pageData.map(p => `
        <div class="bg-white border rounded-2xl overflow-hidden hover:shadow transition">
            <img src="${p.imageUrl}" alt="${p.name}" class="w-full h-44 object-cover" />
            <div class="p-4">
                <h3 class="font-semibold text-lg">${p.name}</h3>
                <div class="text-sm text-gray-500 mb-2">${p.brand} • ${p.year}</div>
                <div class="font-bold text-blue-600 mb-3">${window.formatCurrency(p.price)}</div>
                <button class="w-full py-2 rounded-xl bg-blue-600 text-white hover:bg-blue-700"
                        onclick='window.Cart.addToCart(${JSON.stringify(p)})'>
                    Thêm vào giỏ
                </button>
            </div>
        </div>
    `).join('');

    pageInfo.textContent = `Trang ${state.page}/${totalPages} · ${total} xe`;
    document.getElementById('prev-page').disabled = state.page <= 1;
    document.getElementById('next-page').disabled = state.page >= totalPages;
}

function bindUI() {
    const brand = document.getElementById('filter-brand');
    const price = document.getElementById('filter-price');
    const sort = document.getElementById('sort-by');
    const search = document.getElementById('search-key');

    brand?.addEventListener('change', e => { state.brand = e.target.value; state.page = 1; renderGrid(); });
    price?.addEventListener('change', e => { state.priceRange = e.target.value; state.page = 1; renderGrid(); });
    sort?.addEventListener('change', e => { state.sortBy = e.target.value; state.page = 1; renderGrid(); });
    search?.addEventListener('input', e => { state.searchKey = e.target.value.trim(); state.page = 1; renderGrid(); });

    document.getElementById('prev-page')?.addEventListener('click', () => { state.page--; renderGrid(); });
    document.getElementById('next-page')?.addEventListener('click', () => { state.page++; renderGrid(); });
}

document.addEventListener('DOMContentLoaded', () => {
    bindUI();
    renderGrid();
});
