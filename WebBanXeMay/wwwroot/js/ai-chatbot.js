(function () {
    const widget = document.getElementById("aiChatbotWidget");
    const toggleBtn = document.getElementById("aiChatToggle");
    const panel = document.getElementById("aiChatPanel");
    const closeBtn = document.getElementById("aiChatClose");
    const resetBtn = document.getElementById("aiChatReset");
    const sendBtn = document.getElementById("aiChatSend");
    const input = document.getElementById("aiChatInput");
    const messages = document.getElementById("aiChatMessages");

    if (!widget || !toggleBtn || !panel || !sendBtn || !input || !messages) return;

    const STORAGE_KEY = "ai_chat_conversation_id";
    const HISTORY_KEY = "ai_chat_local_history";
    const MAX_HISTORY = 50;

    let isSending = false;

    function getConversationId() {
        return localStorage.getItem(STORAGE_KEY);
    }

    function setConversationId(id) {
        if (id) {
            localStorage.setItem(STORAGE_KEY, id);
        }
    }

    function clearConversationId() {
        localStorage.removeItem(STORAGE_KEY);
    }

    function getHistory() {
        const raw = localStorage.getItem(HISTORY_KEY);
        if (!raw) return [];

        try {
            const parsed = JSON.parse(raw);
            return Array.isArray(parsed) ? parsed : [];
        } catch {
            return [];
        }
    }

    function saveHistory(history) {
        const normalized = Array.isArray(history) ? history.slice(-MAX_HISTORY) : [];
        localStorage.setItem(HISTORY_KEY, JSON.stringify(normalized));
    }

    function appendHistory(role, content) {
        const history = getHistory();
        history.push({ role, content });
        saveHistory(history);
    }

    function clearHistory() {
        localStorage.removeItem(HISTORY_KEY);
    }

    function scrollBottom() {
        messages.scrollTop = messages.scrollHeight;
    }

    function focusInput() {
        setTimeout(() => input.focus(), 50);
    }

    function setSendingState(sending) {
        isSending = sending;
        input.disabled = sending;
        sendBtn.disabled = sending;

        if (sending) {
            sendBtn.classList.add("disabled");
        } else {
            sendBtn.classList.remove("disabled");
        }
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

    function formatBotMessage(content) {
        if (!content) return "";

        const imageRegex = /!\[(.*?)\]\((.*?)\)/g;
        let textOnly = content;
        let imageHtml = "";
        let match;

        while ((match = imageRegex.exec(content)) !== null) {
            const alt = match[1] || "image";
            const url = match[2] || "";

            if (url) {
                imageHtml += `
                    <div class="ai-product-card">
                        <img src="${escapeAttribute(url)}"
                             alt="${escapeAttribute(alt)}"
                             class="ai-product-image" />
                    </div>
                `;
            }
        }

        textOnly = textOnly.replace(imageRegex, "").trim();

        let safeText = escapeHtml(textOnly).replace(/\n/g, "<br>");
        safeText = formatLinks(safeText);

        return `
            <div class="ai-msg-text">${safeText}</div>
            ${imageHtml}
        `;
    }

    function createWelcomeMessage() {
        return `
            <div class="ai-msg bot ai-msg-welcome">
                <div class="ai-msg-text">
                    Xin chào 👋 Tôi có thể hỗ trợ bạn:
                    <br>- Tra cứu giá xe
                    <br>- Kiểm tra tồn kho
                    <br>- Tư vấn mẫu xe phù hợp
                </div>
            </div>
        `;
    }

    function renderWelcomeMessage() {
        messages.innerHTML = createWelcomeMessage();
        scrollBottom();
    }

    function addMessage(role, content, save = true) {
        const div = document.createElement("div");
        div.className = `ai-msg ${role}`;

        if (role === "bot") {
            div.innerHTML = formatBotMessage(content);
        } else {
            div.innerHTML = escapeHtml(content).replace(/\n/g, "<br>");
        }

        messages.appendChild(div);
        scrollBottom();

        if (save) {
            appendHistory(role, content);
        }
    }

    function addTyping() {
        removeTyping();

        const div = document.createElement("div");
        div.className = "ai-msg bot typing";
        div.id = "aiTyping";
        div.innerHTML = `<div class="ai-msg-text">Bot đang trả lời...</div>`;
        messages.appendChild(div);
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

    async function sendMessage(customMessage) {
        if (isSending) return;

        const message = (customMessage ?? input.value).trim();
        if (!message) return;

        addMessage("user", message);
        input.value = "";
        setSendingState(true);
        addTyping();

        try {
            const response = await fetch("/ai-chat/send", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    message: message,
                    conversationId: getConversationId()
                })
            });

            const result = await safeReadJson(response);
            removeTyping();

            if (!response.ok || !result.success) {
                addMessage("bot", result?.errorMessage || "Đã có lỗi xảy ra khi xử lý yêu cầu.");
                return;
            }

            if (result.conversationId) {
                setConversationId(result.conversationId);
            }

            const replyText = result.reply || "Bot chưa có phản hồi.";
            addMessage("bot", replyText);
        } catch (error) {
            removeTyping();
            console.error("AI Chatbot error:", error);
            addMessage("bot", "Không thể kết nối tới chatbot. Vui lòng thử lại sau.");
        } finally {
            setSendingState(false);
            focusInput();
        }
    }

    function loadLocalHistory() {
        const history = getHistory();

        if (!history.length) {
            renderWelcomeMessage();
            return;
        }

        messages.innerHTML = "";

        history.forEach(item => {
            if (!item || !item.role || typeof item.content !== "string") return;
            addMessage(item.role, item.content, false);
        });

        scrollBottom();
    }

    function openPanel() {
        panel.classList.remove("d-none");
        focusInput();
    }

    function closePanel() {
        panel.classList.add("d-none");
    }

    async function resetConversation() {
        const currentConversationId = getConversationId();

        try {
            if (currentConversationId) {
                const response = await fetch("/ai-chat/reset", {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json"
                    },
                    body: JSON.stringify({
                        conversationId: currentConversationId
                    })
                });

                const result = await safeReadJson(response);

                if (!response.ok || !result.success) {
                    console.error("Reset conversation failed:", result?.errorMessage);
                }
            }
        } catch (error) {
            console.error("Reset conversation error:", error);
        } finally {
            clearConversationId();
            clearHistory();
            renderWelcomeMessage();
            focusInput();
        }
    }

    toggleBtn.addEventListener("click", () => {
        if (panel.classList.contains("d-none")) {
            openPanel();
        } else {
            closePanel();
        }
    });

    if (closeBtn) {
        closeBtn.addEventListener("click", closePanel);
    }

    if (resetBtn) {
        resetBtn.addEventListener("click", resetConversation);
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

    loadLocalHistory();
})();