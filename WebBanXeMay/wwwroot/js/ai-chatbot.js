(function () {
    const widget = document.getElementById("aiChatbotWidget");
    const toggleBtn = document.getElementById("aiChatToggle");
    const panel = document.getElementById("aiChatPanel");
    const closeBtn = document.getElementById("aiChatClose");
    const sendBtn = document.getElementById("aiChatSend");
    const input = document.getElementById("aiChatInput");
    const messages = document.getElementById("aiChatMessages");
    const conversationList = document.getElementById("aiChatConversationList");
    const newChatBtn = document.getElementById("aiChatNewConversation");
    const deleteBtn = document.getElementById("aiChatDeleteConversation");
    const emptyState = document.getElementById("aiChatEmptyState");

    if (!widget || !toggleBtn || !panel || !sendBtn || !input || !messages || !conversationList) return;

    const USER_STORAGE_KEY = "ai_chat_user_id";
    const CURRENT_CONVERSATION_KEY = "ai_chat_current_conversation_id";
    const CHANNEL = "web";

    const state = {
        isSending: false,
        currentConversationId: sessionStorage.getItem(CURRENT_CONVERSATION_KEY) || null,
        conversations: []
    };

    function getUserId() {
        let userId = localStorage.getItem(USER_STORAGE_KEY);
        if (!userId) {
            userId = "web_" + crypto.randomUUID();
            localStorage.setItem(USER_STORAGE_KEY, userId);
        }
        return userId;
    }

    function setCurrentConversationId(id) {
        state.currentConversationId = id || null;

        if (state.currentConversationId) {
            sessionStorage.setItem(CURRENT_CONVERSATION_KEY, state.currentConversationId);
        } else {
            sessionStorage.removeItem(CURRENT_CONVERSATION_KEY);
        }
    }
    async function resetConversationRequest(conversationId) {
        const response = await fetch("/ai-chat/reset", {
            method: "POST",
            headers: {
                "Content-Type": "application/json; charset=utf-8",
                "Accept": "application/json"
            },
            body: JSON.stringify({ conversationId })
        });

        return await safeReadJson(response);
    }

    function scrollBottom() {
        messages.scrollTop = messages.scrollHeight;
    }

    function focusInput() {
        setTimeout(() => input.focus(), 60);
    }

    function setSendingState(sending) {
        state.isSending = sending;
        input.disabled = sending;
        sendBtn.disabled = sending;
        sendBtn.classList.toggle("disabled", sending);
    }

    function escapeHtml(text) {
        const div = document.createElement("div");
        div.innerText = text ?? "";
        return div.innerHTML;
    }

    function escapeAttribute(text) {
        return String(text ?? "")
            .replace(/&/g, "&amp;")
            .replace(/"/g, "&quot;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;");
    }

    function formatLinks(text) {
        const urlRegex = /(https?:\/\/[^\s<]+)/g;
        return text.replace(urlRegex, (url) => {
            const safeUrl = escapeAttribute(url);
            return `<a href="${safeUrl}" target="_blank" rel="noopener noreferrer">${safeUrl}</a>`;
        });
    }
    function formatBasicMarkdown(text) {
        if (!text) return "";

        return text
            .replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>")
            .replace(/\*(.+?)\*/g, "<em>$1</em>");
    }
    function buildProductDetailUrl(product) {
        if (!product) return null;

        if (product.slug && product.slug.trim()) {
            return `/SanPham/Details?slug=${encodeURIComponent(product.slug)}`;
        }

        return null;
    }
    // Thay thế hàm renderProductCards cũ của mày bằng bản này
    function renderProductCards(products) {
        if (!Array.isArray(products) || !products.length) return "";

        return `<div class="ai-shop-card-list">` +
            products.map(p => {
                const name = escapeHtml(p.ten || "Sản phẩm");
                const price = p.gia ? Number(p.gia).toLocaleString("vi-VN") + " ₫" : "Liên hệ";
                const url = buildProductDetailUrl(p);
                const img = (p.imageUrl && p.imageUrl.trim()) ? p.imageUrl : "/images/no-image.png";

                return `
                <a href="${escapeAttribute(url)}" class="ai-shop-card" target="_blank">
                    <div class="ai-shop-card-media">
                        <img src="${escapeAttribute(img)}" class="ai-shop-card-thumb" alt="${name}">
                    </div>
                    <div class="ai-shop-card-body">
                        <div class="ai-shop-card-name">${name}</div>
                        <div class="ai-shop-card-price">${price}</div>
                        <div class="ai-shop-card-link-text">Nhấn để xem chi tiết...</div>
                    </div>
                </a>`;
            }).join("") +
            `</div>`;
    }

    // Sửa hàm formatBotMessage để CHẶN đứng việc render ảnh lung tung
    function formatBotMessage(content, products = []) {
        // 1. Dùng Regex xóa sạch các tag ảnh Markdown ![alt](url) để nó không hiện ảnh to đùng nữa
        let cleanText = (content || "").replace(/!\[.*?\]\(.*?\)/g, "").trim();

        let safeHtml = escapeHtml(cleanText).replace(/\n/g, "<br>");
        safeHtml = formatBasicMarkdown(safeHtml);

        // 2. Render list card gọn gàng bên dưới text
        const cards = renderProductCards(products);

        return `<div class="ai-msg-text">${safeHtml}</div>${cards}`;
    }

    function toggleEmptyState(show) {
        if (!emptyState) return;
        emptyState.classList.toggle("d-none", !show);
    }

    function clearMessages() {
        messages.innerHTML = "";
    }

    function renderWelcomeMessage() {
        clearMessages();
        toggleEmptyState(true);

        const welcome = document.createElement("div");
        welcome.className = "ai-msg bot ai-msg-welcome";
        welcome.innerHTML = `
            <div class="ai-msg-text">
                Xin chào 👋 Mình có thể hỗ trợ bạn:
                <br>- Tra cứu giá xe
                <br>- Kiểm tra tồn kho
                <br>- Tư vấn mẫu xe phù hợp
            </div>
        `;
        messages.appendChild(welcome);
        scrollBottom();
    }

function addMessage(role, content, products = []) {
    const div = document.createElement("div");
    div.className = `ai-msg ${role}`;

    if (role === "bot") {
        div.innerHTML = formatBotMessage(content, products);
    } else {
        div.innerHTML = escapeHtml(content).replace(/\n/g, "<br>");
    }

    messages.appendChild(div);
    toggleEmptyState(false);
    scrollBottom();
}

    function addTyping() {
        removeTyping();

        const div = document.createElement("div");
        div.className = "ai-msg bot typing";
        div.id = "aiTyping";
        div.innerHTML = `<div class="ai-msg-text">Bot đang trả lời...</div>`;
        messages.appendChild(div);
        toggleEmptyState(false);
        scrollBottom();
    }

    function removeTyping() {
        const typing = document.getElementById("aiTyping");
        if (typing) typing.remove();
    }

    async function safeReadJson(response) {
        const contentType = response.headers.get("content-type") || "";

        if (!contentType.includes("application/json")) {
            const text = await response.text();
            throw new Error(text || "Phản hồi từ máy chủ không phải JSON hợp lệ.");
        }

        return await response.json();
    }

    function formatTime(isoString) {
        if (!isoString) return "";
        const date = new Date(isoString);

        return date.toLocaleString("vi-VN", {
            hour: "2-digit",
            minute: "2-digit",
            day: "2-digit",
            month: "2-digit"
        });
    }

    function renderConversationList() {
        if (!state.conversations.length) {
            conversationList.innerHTML = `
                <div class="ai-chat-no-history">Chưa có cuộc trò chuyện nào.</div>
            `;
            return;
        }

        conversationList.innerHTML = state.conversations.map(item => {
            const activeClass = item.conversationId === state.currentConversationId ? "active" : "";
            const title = escapeHtml(item.title || "Đoạn chat mới");
            const preview = escapeHtml(item.lastMessagePreview || "Chưa có nội dung xem trước.");
            const updatedAt = formatTime(item.updatedAtUtc);

            return `
                <button type="button"
                        class="ai-chat-conversation-item ${activeClass}"
                        data-conversation-id="${escapeAttribute(item.conversationId)}">
                    <div class="ai-chat-conversation-title">${title}</div>
                    <div class="ai-chat-conversation-preview">${preview}</div>
                    <div class="ai-chat-conversation-time">${updatedAt}</div>
                </button>
            `;
        }).join("");

        conversationList.querySelectorAll(".ai-chat-conversation-item").forEach(btn => {
            btn.addEventListener("click", async () => {
                const id = btn.dataset.conversationId;
                if (!id) return;

                setCurrentConversationId(id);
                renderConversationList();
                await loadMessages(id);
            });
        });
    }

    async function fetchConversations() {
        const userId = getUserId();
        const response = await fetch(`/ai-chat/conversations?userId=${encodeURIComponent(userId)}&channel=${encodeURIComponent(CHANNEL)}`);
        return await safeReadJson(response);
    }

    async function fetchMessages(conversationId) {
        const response = await fetch(`/ai-chat/conversations/${encodeURIComponent(conversationId)}/messages`);
        return await safeReadJson(response);
    }

    async function sendChatMessage(payload) {
        const response = await fetch("/ai-chat/send", {
            method: "POST",
            headers: {
                "Content-Type": "application/json; charset=utf-8",
                "Accept": "application/json"
            },
            body: JSON.stringify(payload)
        });

        return await safeReadJson(response);
    }

    async function deleteConversationRequest(conversationId) {
        const response = await fetch(`/ai-chat/conversations/${encodeURIComponent(conversationId)}`, {
            method: "DELETE"
        });

        return await safeReadJson(response);
    }

    async function loadConversations() {
        try {
            const result = await fetchConversations();

            if (!result?.success) {
                conversationList.innerHTML = `<div class="ai-chat-no-history">Không thể tải lịch sử chat.</div>`;
                return;
            }

            state.conversations = Array.isArray(result.items) ? result.items : [];
            renderConversationList();
        } catch (error) {
            console.error("Load conversations error:", error);
            conversationList.innerHTML = `<div class="ai-chat-no-history">Không thể tải lịch sử chat.</div>`;
        }
    }

    async function loadMessages(conversationId) {
        try {
            const result = await fetchMessages(conversationId);

            if (!result?.success) {
                renderWelcomeMessage();
                return;
            }

            const items = Array.isArray(result.items) ? result.items : [];

            clearMessages();

            if (!items.length) {
                renderWelcomeMessage();
                return;
            }

            toggleEmptyState(false);

            items.forEach(item => {
                const role = item.role === "user" ? "user" : "bot";
                addMessage(role, item.content || "");
            });
        } catch (error) {
            console.error("Load messages error:", error);
            renderWelcomeMessage();
        }
    }

    async function sendMessage(customMessage) {
        if (state.isSending) return;

        const message = (customMessage ?? input.value).trim();
        if (!message) return;

        addMessage("user", message);
        input.value = "";
        setSendingState(true);
        addTyping();

        try {
            const result = await sendChatMessage({
                message: message,
                conversationId: state.currentConversationId,
                userId: getUserId(),
                channel: CHANNEL
            });

            removeTyping();

            if (result?.conversationId) {
                setCurrentConversationId(result.conversationId);
            }

            const replyText = result?.reply?.trim()
                || "Xin lỗi, hiện tại mình chưa thể phản hồi. Bạn thử lại giúp mình nhé.";

            const products = Array.isArray(result?.products) ? result.products : [];

            addMessage("bot", replyText, products);
            await loadConversations();
        } catch (error) {
            removeTyping();
            console.error("Send message error:", error);
            addMessage("bot", "Không thể kết nối tới chatbot. Vui lòng thử lại sau.");
        } finally {
            setSendingState(false);
            focusInput();
        }
    }

    async function startNewConversation() {
        const oldConversationId = state.currentConversationId;

        try {
            if (oldConversationId) {
                await resetConversationRequest(oldConversationId);
            }
        } catch (error) {
            console.error("Reset conversation error:", error);
        }

        setCurrentConversationId(null);
        input.value = "";
        removeTyping();
        renderConversationList();
        renderWelcomeMessage();
        focusInput();
    }

    async function deleteCurrentConversation() {
        if (!state.currentConversationId) return;

        const confirmed = window.confirm("Bạn có chắc muốn xóa đoạn chat này không?");
        if (!confirmed) return;

        try {
            const result = await deleteConversationRequest(state.currentConversationId);

            if (!result?.success) {
                alert(result?.errorMessage || "Không thể xóa đoạn chat.");
                return;
            }

            setCurrentConversationId(null);
            renderWelcomeMessage();
            await loadConversations();
        } catch (error) {
            console.error("Delete conversation error:", error);
            alert("Không thể xóa đoạn chat.");
        }
    }

    function openPanel() {
        panel.classList.remove("d-none");
        focusInput();
    }

    function closePanel() {
        panel.classList.add("d-none");
    }

    toggleBtn.addEventListener("click", async () => {
        if (panel.classList.contains("d-none")) {
            openPanel();
            await loadConversations();

            if (state.currentConversationId) {
                await loadMessages(state.currentConversationId);
            } else {
                renderWelcomeMessage();
            }
        } else {
            closePanel();
        }
    });

    if (closeBtn) {
        closeBtn.addEventListener("click", closePanel);
    }

    if (newChatBtn) {
        newChatBtn.addEventListener("click", async () => {
            await startNewConversation();
        });
    }

    if (deleteBtn) {
        deleteBtn.addEventListener("click", deleteCurrentConversation);
    }

    sendBtn.addEventListener("click", () => sendMessage());

    input.addEventListener("keydown", function (e) {
        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            sendMessage();
        }
    });

    widget.querySelectorAll(".ai-suggest-btn").forEach(btn => {
        btn.addEventListener("click", () => {
            const msg = btn.dataset.message;
            if (msg) {
                sendMessage(msg);
            }
        });
    });

    renderWelcomeMessage();
})();