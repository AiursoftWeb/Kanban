(function() {
    'use strict';

    var conversationId = null;
    var pollTimer = null;
    var lastMessageCount = 0;
    var token = '';
    var pollInterval = 1000;
    var excelMarkdown = null;
    var attachedFileName = null;

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

        var attachBtn = document.getElementById('agent-attach-btn');
        var excelInput = document.getElementById('agent-excel-input');
        var fileRemoveBtn = document.getElementById('agent-file-remove');

        if (attachBtn && excelInput) {
            attachBtn.addEventListener('click', function() { excelInput.click(); });
            excelInput.addEventListener('change', handleExcelFile);
            if (fileRemoveBtn) {
                fileRemoveBtn.addEventListener('click', clearExcelFile);
            }
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

        // Clear file state on send
        var hasExcel = !!excelMarkdown;
        var body = { boardId: boardId, message: message };
        if (conversationId) {
            body.ConversationId = conversationId;
        }
        if (excelMarkdown) {
            body.ExcelMarkdown = excelMarkdown;
        }
        excelMarkdown = null;
        clearExcelFile();

        // Show thinking immediately — the poll will render the user message
        // and assistant response. No manual DOM append avoids duplication.
        showThinking();

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

    var renderedAdviceIds = [];

    function renderAdvice(data) {
        var container = document.getElementById('agent-messages');
        if (!container) return;

        var pendingIds = (data.PendingAdvice || []).map(function(a) { return a.AdviceId; });

        // Remove cards whose advice is no longer pending (was resolved)
        var existingCards = document.querySelectorAll('.advice-card[data-conversation]');
        existingCards.forEach(function(card) {
            var adviceId = card.getAttribute('data-advice-id');
            if (pendingIds.indexOf(adviceId) === -1) {
                card.remove();
                renderedAdviceIds = renderedAdviceIds.filter(function(id) { return id !== adviceId; });
            }
        });

        if (!data.PendingAdvice || data.PendingAdvice.length === 0) return;

        // Only render advice cards that haven't been rendered yet
        data.PendingAdvice.forEach(function(advice) {
            if (renderedAdviceIds.indexOf(advice.AdviceId) !== -1) return; // Already rendered
            renderedAdviceIds.push(advice.AdviceId);

            var card = document.createElement('div');
            card.className = 'advice-card';
            card.setAttribute('data-conversation', data.ConversationId);
            card.setAttribute('data-advice-id', advice.AdviceId);

            var header = document.createElement('div');
            header.className = 'advice-header';
            header.innerHTML = loc('proposed-action', 'Proposed Action:') + ' <strong>' + escapeHtml(advice.ToolDisplayName) + '</strong>';

            // Structured parameter rows
            if (advice.Parameters && advice.Parameters.length > 0) {
                var paramsDiv = document.createElement('div');
                paramsDiv.className = 'advice-params';
                advice.Parameters.forEach(function(p) {
                    var row = document.createElement('div');
                    row.className = 'advice-param-row';
                    var keySpan = document.createElement('span');
                    keySpan.className = 'advice-param-key';
                    keySpan.textContent = p.DisplayKey;
                    var valSpan = document.createElement('span');
                    valSpan.className = 'advice-param-value';
                    valSpan.textContent = p.Value != null ? p.Value : '';
                    row.appendChild(keySpan);
                    row.appendChild(valSpan);
                    paramsDiv.appendChild(row);
                });
                card.appendChild(header);
                card.appendChild(paramsDiv);
            } else {
                var detail = document.createElement('div');
                detail.className = 'advice-detail';
                detail.textContent = advice.ParameterDisplay;
                card.appendChild(header);
                card.appendChild(detail);
            }

            // Resolved name
            if (advice.ResolvedName) {
                var resolved = document.createElement('div');
                resolved.className = 'advice-resolved';
                resolved.textContent = advice.ResolvedName;
                card.appendChild(resolved);
            }

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
        // Do NOT auto-scroll — let the user control their viewport natively.
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
        renderedAdviceIds = [];
        clearExcelFile();
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

        var statusEl = document.getElementById('agent-status-text');
        if (statusEl) statusEl.textContent = loc('ready', 'Ready');
    }

    function handleExcelFile() {
        var input = document.getElementById('agent-excel-input');
        if (!input || !input.files || input.files.length === 0) return;

        var file = input.files[0];
        var ext = file.name.split('.').pop().toLowerCase();
        if (ext !== 'xlsx') {
            appendMessage('assistant', 'Only .xlsx files are supported. Please convert .xls to .xlsx before uploading.');
            input.value = '';
            return;
        }

        var formData = new FormData();
        formData.append('file', file);

        fetch('/Agent/ConvertExcel', {
            method: 'POST',
            headers: { 'RequestVerificationToken': token },
            body: formData
        })
        .then(function(r) {
            if (!r.ok) return r.json().then(function(d) { throw new Error(d.Error || 'Upload failed'); });
            return r.json();
        })
        .then(function(data) {
            excelMarkdown = data.markdown;
            attachedFileName = data.fileName;
            showFileChip(data.fileName);
        })
        .catch(function(err) {
            appendMessage('assistant', 'Excel upload failed: ' + err.message);
            clearExcelFile();
        });

        input.value = '';
    }

    function showFileChip(name) {
        var chip = document.getElementById('agent-file-chip');
        var nameSpan = document.getElementById('agent-file-name');
        if (chip) chip.style.display = 'flex';
        if (nameSpan) nameSpan.textContent = name;
    }

    function clearExcelFile() {
        excelMarkdown = null;
        attachedFileName = null;
        var chip = document.getElementById('agent-file-chip');
        if (chip) chip.style.display = 'none';
        var input = document.getElementById('agent-excel-input');
        if (input) input.value = '';
    }

    function escapeHtml(text) {
        var div = document.createElement('div');
        div.appendChild(document.createTextNode(text));
        return div.innerHTML;
    }

    window.AgentChat = { init: init };
})();
