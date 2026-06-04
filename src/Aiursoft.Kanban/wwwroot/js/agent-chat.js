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
        var newChatBtn = document.getElementById('agent-new-chat-btn');

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

        if (newChatBtn) {
            newChatBtn.addEventListener('click', function() { resetConversation(); });
        }
    }

    function sendMessage(boardId) {
        var input = document.getElementById('agent-input');
        var message = input.value.trim();
        if (!message) return;

        // Don't send while the agent is processing
        if (conversationId) {
            var statusEl = document.getElementById('agent-status-text');
            if (statusEl && statusEl.textContent === loc('thinking-status', 'Thinking...')) return;
        }

        input.value = '';

        // Show thinking immediately — the poll will render the user message
        // and assistant response. No manual DOM append avoids duplication.
        showThinking();

        var body = { boardId: boardId, message: message };
        if (conversationId) {
            body.ConversationId = conversationId;
        }

        fetch('/Agent/SendMessage', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token
            },
            body: JSON.stringify(body)
        })
        .then(function(r) { return r.json(); })
        .then(function(data) {
            if (data.Error) {
                hideThinking();
                appendMessage('assistant', loc('error-prefix', 'Error:') + ' ' + data.Error);
                return;
            }
            conversationId = data.ConversationId;
            pollStatus(); // Immediate first poll
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
                if (data.Error) { stopPolling(); return; }
                renderMessages(data);
                renderAdvice(data);
                updateState(data.State);

                if (data.State === 'Error') {
                    stopPolling();
                    conversationId = null;
                    if (data.ErrorMessage) {
                        appendMessage('assistant', loc('error-prefix', 'Error:') + ' ' + data.ErrorMessage);
                    } else {
                        appendMessage('assistant', loc('error', 'Error'));
                    }
                } else if (data.State === 'Completed') {
                    // Keep conversationId so the user can continue the conversation.
                    // Only stop polling — the next message resumes the same conversation.
                    stopPolling();
                }
            })
            .catch(function() { /* retry on next poll */ });
    }

    function renderMessages(data) {
        var container = document.getElementById('agent-messages');
        if (!container || !data.Messages) return;

        for (var i = lastMessageCount; i < data.Messages.length; i++) {
            var msg = data.Messages[i];

            if (msg.Role === 'assistant' && msg.ToolCalls && msg.ToolCalls.length > 0 && !msg.Content) {
                continue;
            }

            if (msg.Role === 'tool') {
                continue;
            }

            appendMessage(msg.Role, msg.Content);
        }
        lastMessageCount = data.Messages.length;
    }

    function renderAdvice(data) {
        var oldCards = document.querySelectorAll('.advice-card[data-conversation]');
        oldCards.forEach(function(card) { card.remove(); });

        if (!data.PendingAdvice || data.PendingAdvice.length === 0) return;

        var container = document.getElementById('agent-messages');
        if (!container) return;

        data.PendingAdvice.forEach(function(advice) {
            var card = document.createElement('div');
            card.className = 'advice-card';
            card.setAttribute('data-conversation', data.ConversationId);
            card.setAttribute('data-advice-id', advice.AdviceId);

            var header = document.createElement('div');
            header.className = 'advice-header';
            header.textContent = loc('proposed-action', 'Proposed Action:') + ' ' + advice.ToolDisplayName;

            var detail = document.createElement('div');
            detail.className = 'advice-detail';
            detail.textContent = advice.ParameterDisplay;

            var actions = document.createElement('div');
            actions.className = 'advice-actions';

            var approveBtn = document.createElement('button');
            approveBtn.className = 'btn btn-sm btn-success';
            approveBtn.textContent = loc('approve', 'Approve');
            approveBtn.addEventListener('click', function() {
                approveAdvice(advice.AdviceId, card);
            });

            var rejectBtn = document.createElement('button');
            rejectBtn.className = 'btn btn-sm btn-outline-danger';
            rejectBtn.textContent = loc('reject', 'Reject');
            rejectBtn.addEventListener('click', function() {
                rejectAdvice(advice.AdviceId, card);
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
            // Keep lastMessageCount so only new messages are rendered — no duplicates
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
            // Keep lastMessageCount so only new messages are rendered — no duplicates
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
        var bar = document.getElementById('agent-thinking-bar');
        if (bar) {
            bar.style.display = 'flex';
        }
    }

    function hideThinking() {
        var bar = document.getElementById('agent-thinking-bar');
        if (bar) {
            bar.style.display = 'none';
        }
    }

    function updateState(state) {
        var statusEl = document.getElementById('agent-status-text');

        if (state === 'Error') {
            if (statusEl) statusEl.textContent = loc('error', 'Error');
            hideThinking();
        } else if (state === 'Completed') {
            if (statusEl) statusEl.textContent = loc('ready', 'Ready');
            hideThinking();
        } else if (state === 'AwaitingApproval') {
            if (statusEl) statusEl.textContent = loc('waiting-approval', 'Waiting for approval');
            hideThinking();
        } else if (state === 'Thinking') {
            if (statusEl) statusEl.textContent = loc('thinking-status', 'Thinking...');
        }
    }

    function resetConversation() {
        if (conversationId) {
            fetch('/Agent/Cancel?conversationId=' + conversationId, {
                method: 'POST',
                headers: { 'RequestVerificationToken': token }
            }).catch(function() {});
        }
        conversationId = null;
        lastMessageCount = 0;
        stopPolling();
        hideThinking();

        // Restore welcome message
        var container = document.getElementById('agent-messages');
        if (container) {
            container.innerHTML = '';
            var welcome = document.createElement('div');
            welcome.className = 'chat-message assistant';
            welcome.textContent = loc('welcome', 'Hi!');
            container.appendChild(welcome);
        }

        var container = document.getElementById('agent-messages');
        if (container) container.innerHTML = '';

        var statusEl = document.getElementById('agent-status-text');
        if (statusEl) statusEl.textContent = loc('ready', 'Ready');
    }

    window.AgentChat = { init: init };
})();
