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
    const humanSupportBtn = document.getElementById("aiChatHumanSupport");

    if (!widget || !toggleBtn || !panel || !sendBtn || !input || !messages || !conversationList) return;

    const USER_STORAGE_KEY = "ai_chat_user_id";
    const CURRENT_CONVERSATION_KEY = "ai_chat_current_conversation_id";
    const CHANNEL = "web";
    const AUTH_STATE_KEY = "ai_chat_auth_state";
    const AUTH_USER_KEY = "ai_chat_auth_user_id";

    const state = {
        isSending: false,
        currentConversationId: sessionStorage.getItem(CURRENT_CONVERSATION_KEY) || null,
        conversations: [],
    };

    function isAuthenticatedUser() {
        return document.getElementById("__isAuth")?.value === "1";
    }

    function getRealUserId() {
        return document.getElementById("__currentUserId")?.value || "";
    }

    function getUserId() {
        if (isAuthenticatedUser()) {
            return getRealUserId();
        }

        let userId = localStorage.getItem(USER_STORAGE_KEY);
        if (!userId) {
            userId = "web_" + crypto.randomUUID();
            localStorage.setItem(USER_STORAGE_KEY, userId);
        }

        return userId;
    }

    function getAuthState() {
        return isAuthenticatedUser() ? "1" : "0";
    }

    function clearLocalChatSession() {
        sessionStorage.removeItem(CURRENT_CONVERSATION_KEY);
        state.currentConversationId = null;
    }

    function syncAuthSessionState() {
        const currentAuthState = getAuthState();
        const currentAuthUserId = getRealUserId();

        const previousAuthState = sessionStorage.getItem(AUTH_STATE_KEY);
        const previousAuthUserId = sessionStorage.getItem(AUTH_USER_KEY);

        if (
            previousAuthState !== null &&
            (previousAuthState !== currentAuthState || previousAuthUserId !== currentAuthUserId)
        ) {
            clearLocalChatSession();
        }

        sessionStorage.setItem(AUTH_STATE_KEY, currentAuthState);
        sessionStorage.setItem(AUTH_USER_KEY, currentAuthUserId || "");
    }

    syncAuthSessionState();

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
                Accept: "application/json",
            },
            body: JSON.stringify({ conversationId }),
        });

        return await safeReadJson(response);
    }

    function scrollBottom() {
        messages.scrollTop = messages.scrollHeight;
    }

    function focusInput() {
        setTimeout(() => input.focus(), 60);
    }
    function openHumanSupport() {
        const targetUrl = "/Chat/Index";

        closePanel();

        setTimeout(() => {
            if (!isAuthenticatedUser()) {
                if (typeof window.openAuthModal === "function") {
                    window.openAuthModal(targetUrl);
                    return;
                }

                window.location.href = `/Identity/Account/Login?returnUrl=${encodeURIComponent(targetUrl)}`;
                return;
            }

            window.location.href = targetUrl;
        }, 120);
    }
    function shouldSuggestHumanSupport(message, replyText) {
        const text = `${message || ""} ${replyText || ""}`.toLowerCase();

        const keywords = [
            "đơn hàng",
            "don hang",
            "mã đơn",
            "ma don",
            "giao hàng",
            "giao hang",
            "vận chuyển",
            "van chuyen",
            "bảo hành",
            "bao hanh",
            "đổi trả",
            "doi tra",
            "hoàn tiền",
            "hoan tien",
            "khiếu nại",
            "khieu nai",
            "lỗi xe",
            "loi xe",
            "thanh toán",
            "thanh toan",
            "nhân viên",
            "admin",
            "tư vấn viên",
            "tu van vien"
        ];

        return keywords.some(k => text.includes(k));
    }

    function addHumanSupportSuggestion() {
        const div = document.createElement("div");
        div.className = "ai-msg bot ai-msg-human-suggest";
        div.innerHTML = `
        <div class="ai-human-support-card">
            <div class="ai-human-support-title">Cần nhân viên hỗ trợ?</div>
            <div class="ai-human-support-text">
                Nhân viên sẽ kiểm tra chi tiết hơn về đơn hàng, bảo hành hoặc thanh toán.
            </div>
            <button type="button" class="ai-human-support-btn" data-human-support="true">
                Gặp nhân viên
            </button>
        </div>
    `;

        messages.appendChild(div);
        toggleEmptyState(false);
        scrollBottom();
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

    function renderMarkdownSafe(content) {
        if (!content) return "";

        const codeBlocks = [];
        const inlineCodes = [];

        let safe = escapeHtml(content).replace(/\r\n?/g, "\n");

        safe = safe.replace(/```([\s\S]*?)```/g, (_, code) => {
            const token = `__AI_CODE_BLOCK_${codeBlocks.length}__`;
            const trimmed = (code || "").replace(/^\n+|\n+$/g, "");
            codeBlocks.push(`<pre><code>${trimmed}</code></pre>`);
            return token;
        });

        safe = safe.replace(/`([^`\n]+)`/g, (_, code) => {
            const token = `__AI_INLINE_CODE_${inlineCodes.length}__`;
            inlineCodes.push(`<code>${code}</code>`);
            return token;
        });

        safe = formatLinks(safe);

        safe = safe
            .replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>")
            .replace(/__(.+?)__/g, "<strong>$1</strong>")
            .replace(/(^|\s)\*(?!\s)([^*]+?)\*(?=\s|$)/g, "$1<em>$2</em>")
            .replace(/(^|\s)_(?!\s)([^_]+?)_(?=\s|$)/g, "$1<em>$2</em>");

        const lines = safe.split("\n");
        const htmlParts = [];
        let inUnordered = false;
        let inOrdered = false;

        const closeLists = () => {
            if (inUnordered) {
                htmlParts.push("</ul>");
                inUnordered = false;
            }
            if (inOrdered) {
                htmlParts.push("</ol>");
                inOrdered = false;
            }
        };

        const isTableRow = (line) => /^\|.+\|$/.test(line.trim());
        const isTableSeparator = (line) => {
            const value = line.trim();
            if (!isTableRow(value)) return false;

            const cells = splitTableRow(value);
            return cells.length > 0 && cells.every((cell) => /^:?-{3,}:?$/.test(cell.trim()));
        };
        const splitTableRow = (line) => {
            const value = line.trim().replace(/^\|/, "").replace(/\|$/, "");
            return value.split("|").map((cell) => cell.trim());
        };
        const buildTable = (headerLine, separatorLine, bodyLines) => {
            const headers = splitTableRow(headerLine);
            const separators = splitTableRow(separatorLine);
            const aligns = separators.map((cell) => {
                const value = cell.trim();
                if (value.startsWith(":") && value.endsWith(":")) return "center";
                if (value.endsWith(":")) return "right";
                return "left";
            });

            const thead = headers
                .map((cell, index) => `<th class="align-${aligns[index] || "left"}">${cell}</th>`)
                .join("");

            const rows = bodyLines
                .map((row) => {
                    const cells = splitTableRow(row);
                    return `<tr>${headers
                        .map((_, index) => {
                            const value = cells[index] || "";
                            return `<td class="align-${aligns[index] || "left"}">${value}</td>`;
                        })
                        .join("")}</tr>`;
                })
                .join("");

            return `<div class="ai-table-wrap"><table class="ai-compare-table"><thead><tr>${thead}</tr></thead><tbody>${rows}</tbody></table></div>`;
        };

        for (let i = 0; i < lines.length; i++) {
            const rawLine = lines[i];
            const line = rawLine.trim();

            if (!line) {
                closeLists();
                continue;
            }

            if (isTableRow(line) && i + 1 < lines.length && isTableSeparator(lines[i + 1])) {
                closeLists();

                const headerLine = line;
                const separatorLine = lines[i + 1].trim();
                const bodyLines = [];
                i += 2;

                while (i < lines.length && isTableRow(lines[i]) && !isTableSeparator(lines[i])) {
                    bodyLines.push(lines[i].trim());
                    i++;
                }

                i--;

                if (bodyLines.length > 0) {
                    htmlParts.push(buildTable(headerLine, separatorLine, bodyLines));
                    continue;
                }
            }

            if (/^>\s+/.test(line)) {
                closeLists();
                htmlParts.push(`<blockquote>${line.replace(/^>\s+/, "")}</blockquote>`);
                continue;
            }

            const unorderedMatch = line.match(/^-\s+(.+)/);
            if (unorderedMatch) {
                if (inOrdered) {
                    htmlParts.push("</ol>");
                    inOrdered = false;
                }
                if (!inUnordered) {
                    htmlParts.push("<ul>");
                    inUnordered = true;
                }
                htmlParts.push(`<li>${unorderedMatch[1]}</li>`);
                continue;
            }

            const orderedMatch = line.match(/^\d+\.\s+(.+)/);
            if (orderedMatch) {
                if (inUnordered) {
                    htmlParts.push("</ul>");
                    inUnordered = false;
                }
                if (!inOrdered) {
                    htmlParts.push("<ol>");
                    inOrdered = true;
                }
                htmlParts.push(`<li>${orderedMatch[1]}</li>`);
                continue;
            }

            closeLists();
            htmlParts.push(`<p>${line}</p>`);
        }

        closeLists();

        let html = htmlParts.join("");

        codeBlocks.forEach((block, index) => {
            html = html.replace(`__AI_CODE_BLOCK_${index}__`, block);
        });

        inlineCodes.forEach((code, index) => {
            html = html.replace(`__AI_INLINE_CODE_${index}__`, code);
        });

        return html;
    }

    function formatPrice(value) {
        const numeric = Number(value);
        if (!Number.isFinite(numeric)) return "Liên hệ";
        return `${numeric.toLocaleString("vi-VN")} VNĐ`;
    }

    function buildProductDetailUrl(product) {
        const explicitUrl = String(product?.productUrl ?? product?.ProductUrl ?? "").trim();
        if (explicitUrl) return explicitUrl;

        const slug = String(product?.slug ?? product?.Slug ?? "").trim();
        const id = product?.id ?? product?.Id;

        if (slug) {
            return `/SanPham/Details?slug=${encodeURIComponent(slug)}`;
        }

        if (id) {
            return `/SanPham/Details?id=${encodeURIComponent(id)}`;
        }

        return null;
    }

    function buildProductImageUrl(product) {
        const raw = String(product?.imageUrl ?? product?.ImageUrl ?? "").trim();
        if (!raw) return "/images/no-image.png";

        try {
            const url = new URL(raw, window.location.origin);
            const isLocalhost = url.hostname === "localhost" || url.hostname === "127.0.0.1";

            if (isLocalhost && window.location.hostname !== url.hostname) {
                return `${url.pathname}${url.search}`;
            }

            return url.href;
        } catch {
            return raw.startsWith("/") ? raw : `/${raw}`;
        }
    }

    function renderProductCards(products) {
        if (!Array.isArray(products) || products.length === 0) return "";

        return (
            `<div class="ai-shop-card-list">` +
            products
                .map((product) => {
                    const name = String(product?.ten ?? product?.Ten ?? "Sản phẩm").trim();
                    const price = formatPrice(product?.gia ?? product?.Gia);
                    const safeImageUrl = buildProductImageUrl(product);
                    const detailUrl = buildProductDetailUrl(product);

                    const openTag = detailUrl
                        ? `<a href="${escapeAttribute(detailUrl)}" class="ai-shop-card" target="_blank" rel="noopener noreferrer">`
                        : `<div class="ai-shop-card">`;

                    const closeTag = detailUrl ? "</a>" : "</div>";
                    const linkHint = detailUrl
                        ? `<div class="ai-shop-card-link-text">Nhấn để xem chi tiết...</div>`
                        : "";

                    return `
            ${openTag}
              <div class="ai-shop-card-media">
                <img src="${escapeAttribute(safeImageUrl)}" class="ai-shop-card-thumb" alt="${escapeAttribute(name)}" onerror="this.onerror=null;this.src='/images/no-image.png';" />
              </div>
              <div class="ai-shop-card-body">
                <div class="ai-shop-card-name">${escapeHtml(name)}</div>
                <div class="ai-shop-card-price">${escapeHtml(price)}</div>
                ${linkHint}
              </div>
            ${closeTag}
          `;
                })
                .join("") +
            `</div>`
        );
    }

    function formatBotMessage(content, products = []) {
        const textOnly = String(content || "")
            .replace(/!\[(.*?)\]\((.*?)\)/g, "")
            .trim();

        const safeText = renderMarkdownSafe(textOnly);
        const productHtml = renderProductCards(products);

        if (!safeText) return productHtml;

        return `<div class="ai-msg-text">${safeText}</div>${productHtml}`;
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
            month: "2-digit",
        });
    }

    function renderConversationList() {
        if (!state.conversations.length) {
            conversationList.innerHTML = `
        <div class="ai-chat-no-history">Chưa có cuộc trò chuyện nào.</div>
      `;
            return;
        }

        conversationList.innerHTML = state.conversations
            .map((item) => {
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
            })
            .join("");

        conversationList.querySelectorAll(".ai-chat-conversation-item").forEach((btn) => {
            btn.addEventListener("click", async () => {
                if (!isAuthenticatedUser()) {
                    clearLocalChatSession();
                    renderWelcomeMessage();
                    return;
                }

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

        const response = await fetch(
            `/ai-chat/conversations?userId=${encodeURIComponent(userId)}&channel=${encodeURIComponent(CHANNEL)}`
        );

        return await safeReadJson(response);
    }

    async function fetchMessages(conversationId) {
        const response = await fetch(
            `/ai-chat/conversations/${encodeURIComponent(conversationId)}/messages`
        );

        return await safeReadJson(response);
    }

    async function sendChatMessage(payload) {
        const response = await fetch("/ai-chat/send", {
            method: "POST",
            headers: {
                "Content-Type": "application/json; charset=utf-8",
                Accept: "application/json",
            },
            body: JSON.stringify(payload),
        });

        return await safeReadJson(response);
    }

    async function deleteConversationRequest(conversationId) {
        const response = await fetch(
            `/ai-chat/conversations/${encodeURIComponent(conversationId)}`,
            {
                method: "DELETE",
            }
        );

        return await safeReadJson(response);
    }

    async function loadConversations() {
        if (!isAuthenticatedUser()) {
            state.conversations = [];
            renderConversationList();
            return;
        }

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

            items.forEach((item) => {
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
                channel: CHANNEL,
                isAuthenticated: isAuthenticatedUser(),
            });

            removeTyping();

            if (result?.conversationId) {
                setCurrentConversationId(result.conversationId);
            }

            const replyText =
                result?.reply?.trim() ||
                "Xin lỗi, hiện tại mình chưa thể phản hồi. Bạn thử lại giúp mình nhé.";

            const products = Array.isArray(result?.products) ? result.products : [];

            addMessage("bot", replyText, products);

            if (shouldSuggestHumanSupport(message, replyText)) {
                addHumanSupportSuggestion();
            }

            if (isAuthenticatedUser()) {
                await loadConversations();
            }
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

            if (isAuthenticatedUser()) {
                await loadConversations();
            }
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

            if (!isAuthenticatedUser()) {
                clearLocalChatSession();
                state.conversations = [];
                renderConversationList();
                renderWelcomeMessage();
                return;
            }

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
    if (humanSupportBtn) {
        humanSupportBtn.addEventListener("click", openHumanSupport);
    }
    messages.addEventListener("click", function (e) {
        const btn = e.target.closest("[data-human-support='true']");
        if (btn) {
            openHumanSupport();
        }
    });

    sendBtn.addEventListener("click", () => sendMessage());

    input.addEventListener("keydown", function (e) {
        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            sendMessage();
        }
    });

    widget.querySelectorAll(".ai-suggest-btn").forEach((btn) => {
        btn.addEventListener("click", () => {
            const msg = btn.dataset.message;
            if (msg) {
                sendMessage(msg);
            }
        });
    });

    renderWelcomeMessage();
})();
