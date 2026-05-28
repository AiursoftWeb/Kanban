(function() {
    'use strict';

    var conversationId = null;
    var pollTimer = null;
    var lastMessageCount = 0;
    var token = '';
    var pollInterval = 1000;

    function loc(key, fallback) {
        var el = document.querySelector('#agent-loc-data span[data-key="' + key + '"]');
        return el ? el.innerText : (fallback || key);
    }

    function init(antiForgeryToken, boardId) {
        token = antiForgeryToken;
        var widget = document.getElementById('agent-chat-widget');
        if (!widget) return;

        var sendBtn = document.getElementById('agent-send-btn');
        var input = document.getElementById('agent-input');
        var header = widget.querySelector('.agent-chat-header');

        header.addEventListener('click', function() {
            widget.classList.toggle('collapsed');
        });

        sendBtn.addEventListener('click', function() { sendMessage(boardId); });
        input.addEventListener('keydown', function(ev) {
            if (ev.key === 'Enter' && !ev.shiftKey) {
                ev.preventDefault();
                sendMessage(boardId);
            }
        });
    }

    function sendMessage(boardId) {
        var input = document.getElementById('agent-input');
        var message = input.value.trim();
        if (!message || conversationId) return;

        input.value = '';
        lastMessageCount = 1; // Skip user message already appended above
        appendMessage('user', message);
        showThinking();

        fetch('/Agent/SendMessage', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token
            },
            body: JSON.stringify({ boardId: boardId, message: message })
        })
        .then(function(r) { return r.json(); })
        .then(function(data) {
            if (data.error) {
                hideThinking();
                appendMessage('assistant', loc('error-prefix', 'Error:') + ' ' + data.error);
                return;
            }
            conversationId = data.conversationId;
            startPolling();
        })
        .catch(function(err) {
            hideThinking();
            appendMessage('assistant', loc('network-error', 'Network error:') + ' ' + err.message);
        });
    }

    function startPolling() {
        if (pollTimer) clearInterval(pollTimer);
        pollTimer = setInterval(pollStatus, pollInterval);
    }

    function stopPolling() {
        if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
    }

    function pollStatus() {
        if (!conversationId) { stopPolling(); return; }

        fetch('/Agent/Status?conversationId=' + conversationId)
            .then(function(r) { return r.json(); })
            .then(function(data) {
                if (data.error) { stopPolling(); return; }
                renderMessages(data);
                renderAdvice(data);
                updateState(data.state);

                if (data.state === 'Completed' || data.state === 'Error') {
                    stopPolling();
                    conversationId = null;
                    if (data.errorMessage) {
                        appendMessage('assistant', loc('error-prefix', 'Error:') + ' ' + data.errorMessage);
                    }
                }
            })
            .catch(function() { /* retry on next poll */ });
    }

    function renderMessages(data) {
        var container = document.getElementById('agent-messages');
        if (!container || !data.messages) return;

        for (var i = lastMessageCount; i < data.messages.length; i++) {
            var msg = data.messages[i];

            if (msg.role === 'assistant' && msg.toolCalls && msg.toolCalls.length > 0 && !msg.content) {
                continue;
            }

            if (msg.role === 'tool') {
                continue;
            }

            appendMessage(msg.role, msg.content);
        }
        lastMessageCount = data.messages.length;
    }

    function renderAdvice(data) {
        var oldCards = document.querySelectorAll('.advice-card[data-conversation]');
        oldCards.forEach(function(card) { card.remove(); });

        if (!data.pendingAdvice || data.pendingAdvice.length === 0) return;

        var container = document.getElementById('agent-messages');
        if (!container) return;

        data.pendingAdvice.forEach(function(advice) {
            var card = document.createElement('div');
            card.className = 'advice-card';
            card.setAttribute('data-conversation', data.conversationId);
            card.setAttribute('data-advice-id', advice.adviceId);

            var header = document.createElement('div');
            header.className = 'advice-header';
            header.textContent = loc('proposed-action', 'Proposed Action:') + ' ' + advice.toolDisplayName;

            var detail = document.createElement('div');
            detail.className = 'advice-detail';
            detail.textContent = advice.parameterDisplay;

            var actions = document.createElement('div');
            actions.className = 'advice-actions';

            var approveBtn = document.createElement('button');
            approveBtn.className = 'btn btn-sm btn-success';
            approveBtn.textContent = loc('approve', 'Approve');
            approveBtn.addEventListener('click', function() {
                approveAdvice(advice.adviceId, card);
            });

            var rejectBtn = document.createElement('button');
            rejectBtn.className = 'btn btn-sm btn-outline-danger';
            rejectBtn.textContent = loc('reject', 'Reject');
            rejectBtn.addEventListener('click', function() {
                rejectAdvice(advice.adviceId, card);
            });

            actions.appendChild(approveBtn);
            actions.appendChild(rejectBtn);

            card.appendChild(header);
            card.appendChild(detail);
            card.appendChild(actions);
            container.appendChild(card);
        });
    }

    function approveAdvice(adviceId, cardElement) {
        if (!conversationId) return;
        disableAdviceButtons(cardElement);

        fetch('/Agent/ApproveAdvice?conversationId=' + conversationId + '&adviceId=' + adviceId, {
            method: 'POST',
            headers: { 'RequestVerificationToken': token }
        })
        .then(function() {
            showResult(cardElement, true, loc('approved-executing', 'Approved - executing...'));
            lastMessageCount = 0; // Reset to get new messages after tool execution
            startPolling();
        });
    }

    function rejectAdvice(adviceId, cardElement) {
        if (!conversationId) return;
        disableAdviceButtons(cardElement);

        fetch('/Agent/RejectAdvice?conversationId=' + conversationId + '&adviceId=' + adviceId, {
            method: 'POST',
            headers: { 'RequestVerificationToken': token }
        })
        .then(function() {
            showResult(cardElement, false, loc('rejected', 'Rejected'));
            lastMessageCount = 0; // Reset to get new messages after rejection
            startPolling();
        });
    }

    function disableAdviceButtons(cardElement) {
        var buttons = cardElement.querySelectorAll('button');
        buttons.forEach(function(b) { b.disabled = true; });
    }

    function showResult(cardElement, success, text) {
        var result = document.createElement('div');
        result.className = 'advice-result ' + (success ? 'success' : 'failure');
        result.textContent = text;
        cardElement.appendChild(result);
    }

    function appendMessage(role, content) {
        if (!content) return;
        var container = document.getElementById('agent-messages');
        if (!container) return;

        var div = document.createElement('div');
        div.className = 'chat-message ' + role;
        div.textContent = content;
        container.appendChild(div);
        container.scrollTop = container.scrollHeight;
    }

    function showThinking() {
        var container = document.getElementById('agent-messages');
        if (!container) return;

        var indicator = document.createElement('div');
        indicator.className = 'agent-thinking-indicator';
        indicator.id = 'agent-thinking';
        indicator.innerHTML = '<span>' + loc('thinking', 'Thinking...') + '</span><span class="dot"></span><span class="dot"></span><span class="dot"></span>';
        container.appendChild(indicator);
        container.scrollTop = container.scrollHeight;
    }

    function hideThinking() {
        var el = document.getElementById('agent-thinking');
        if (el) el.remove();
    }

    function updateState(state) {
        var statusEl = document.getElementById('agent-status-text');
        if (!statusEl) return;

        if (state === 'Thinking') {
            statusEl.textContent = loc('thinking', 'Thinking...');
        } else if (state === 'AwaitingApproval') {
            statusEl.textContent = loc('waiting-approval', 'Waiting for approval');
            hideThinking();
        } else if (state === 'Completed') {
            statusEl.textContent = loc('ready', 'Ready');
            hideThinking();
        } else if (state === 'Error') {
            statusEl.textContent = loc('error', 'Error');
            hideThinking();
        }
    }

    window.AgentChat = { init: init };
})();
