    function getLocalizedText(key, defaultText) {
        var el = document.querySelector('#loc-data span[data-key="' + key + '"]');
        return el ? el.innerText : (defaultText || key);
    }

    document.addEventListener("DOMContentLoaded", function() {
        lucide.createIcons();
        if (window.mermaid) {
            mermaid.initialize({
                startOnLoad: false,
                securityLevel: "strict"
            });
        }

        var boardList = document.getElementById("boardList");
        if (boardList) {
            boardList.addEventListener("click", function(event) {
                var target = event.target;
                if (!target.matches('a, a *, button, button *, .drag-handle, .drag-handle *')) {
                    var row = target.closest(".clickable-row");
                    if (row && row.dataset.href) {
                        window.location.href = row.dataset.href;
                    }
                }
            });

            var boardListBody = document.getElementById("boardListBody");
            if (boardListBody) {
                new Sortable(boardListBody, {
                    animation: 200,
                    easing: "cubic-bezier(0.25, 0.46, 0.45, 0.94)",
                    handle: ".drag-handle",
                    ghostClass: "sortable-ghost",
                    chosenClass: "sortable-chosen",
                    onEnd: function(evt) {
                        var boardId = parseInt(evt.item.dataset.boardId, 10);
                        var newOrder = evt.newIndex;

                        fetch("/Kanban/MoveBoard", {
                            method: "POST",
                            headers: {
                                "Content-Type": "application/x-www-form-urlencoded",
                                "RequestVerificationToken": csrfToken
                            },
                            body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken) + "&boardId=" + boardId + "&newOrder=" + newOrder
                        }).catch(function(err) {
                            console.error("MoveBoard failed:", err);
                        });
                    }
                });
            }
        }

        var csrfToken = window.kanbanCsrfToken;
        var kanbanImageUploadUrl = window.kanbanImageUploadUrl;
        var canEditCurrentBoard = window.kanbanCanEditCurrentBoard;
        var currentBoardId = window.kanbanCurrentBoardId;
        var isDragging = false;
        var labelSearchTimeout = 0;
        var friendlyAlertModalEl = document.getElementById("friendlyAlertModal");
        var friendlyAlertModal = friendlyAlertModalEl ? new bootstrap.Modal(friendlyAlertModalEl) : null;
        var friendlyAlertTitle = document.getElementById("friendlyAlertTitle");
        var friendlyAlertMessage = document.getElementById("friendlyAlertMessage");
        var friendlyAlertIcon = document.getElementById("friendlyAlertIcon");

        function clearInlineAlert(alertEl) {
            if (!alertEl) return;
            alertEl.innerHTML = "";
            alertEl.classList.add("d-none");
        }

        function showInlineAlert(alertEl, message, iconName) {
            if (!alertEl) return;
            alertEl.innerHTML = "";

            var row = document.createElement("div");
            row.className = "d-flex align-items-start";

            var iconWrap = document.createElement("div");
            iconWrap.className = "alert-icon pe-3";

            var icon = document.createElement("i");
            icon.className = "align-middle";
            icon.setAttribute("data-lucide", iconName || "alert-circle");
            iconWrap.appendChild(icon);

            var messageWrap = document.createElement("div");
            messageWrap.className = "alert-message";
            messageWrap.textContent = message || "";

            row.appendChild(iconWrap);
            row.appendChild(messageWrap);
            alertEl.appendChild(row);
            alertEl.classList.remove("d-none");
            lucide.createIcons({ nodes: [alertEl] });
        }

        function showFriendlyDialog(message, title, iconName) {
            if (!friendlyAlertModal || !friendlyAlertMessage || !friendlyAlertTitle || !friendlyAlertIcon) return;
            friendlyAlertTitle.textContent = title || getLocalizedText("notice", "Notice");
            friendlyAlertMessage.textContent = message || "";
            friendlyAlertIcon.setAttribute("data-lucide", iconName || "alert-circle");
            lucide.createIcons({ nodes: [friendlyAlertModalEl] });
            friendlyAlertModal.show();
        }

        var deleteBoardConfirmModalEl = document.getElementById("deleteBoardConfirmModal");
        var deleteBoardConfirmModal = deleteBoardConfirmModalEl ? new bootstrap.Modal(deleteBoardConfirmModalEl) : null;
        var btnConfirmDeleteBoard = document.getElementById("btnConfirmDeleteBoard");
        var deleteBoardError = document.getElementById("deleteBoardError");
        var deleteBoardNameDisplay = document.getElementById("deleteBoardNameDisplay");
        var boardIdToDelete = null;
        var btnToDeleteBoard = null;

        document.querySelectorAll(".btn-delete-board-list").forEach(function(btn) {
            btn.addEventListener("click", function() {
                boardIdToDelete = this.dataset.boardId;
                btnToDeleteBoard = this;
                var boardName = this.dataset.boardName;

                if (deleteBoardNameDisplay) {
                    deleteBoardNameDisplay.textContent = boardName;
                }
                if (deleteBoardError) {
                    clearInlineAlert(deleteBoardError);
                }
                if (btnConfirmDeleteBoard) {
                    btnConfirmDeleteBoard.disabled = false;
                    btnConfirmDeleteBoard.textContent = getLocalizedText("delete-board", "Delete Board");
                }
                if (deleteBoardConfirmModal) {
                    deleteBoardConfirmModal.show();
                }
            });
        });

        if (btnConfirmDeleteBoard) {
            btnConfirmDeleteBoard.addEventListener("click", function() {
                if (!boardIdToDelete) return;

                btnConfirmDeleteBoard.disabled = true;
                btnConfirmDeleteBoard.textContent = getLocalizedText("deleting", "Deleting...");
                if (deleteBoardError) {
                    clearInlineAlert(deleteBoardError);
                }

                fetch("/Kanban/DeleteBoard", {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/x-www-form-urlencoded",
                        "RequestVerificationToken": csrfToken
                    },
                    body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken) + "&boardId=" + boardIdToDelete
                })
                .then(function(r) {
                    if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("server-error", "Server error") + " " + r.status); });
                    if (deleteBoardConfirmModal) {
                        deleteBoardConfirmModal.hide();
                    }
                    if (btnToDeleteBoard) {
                        var row = btnToDeleteBoard.closest("tr");
                        if (row) row.remove();
                    }
                    var tbody = document.querySelector("tbody");
                    if (tbody && tbody.querySelectorAll("tr").length === 0) {
                        window.location.reload();
                    }
                })
                .catch(function(err) {
                    if (deleteBoardError) {
                        showInlineAlert(deleteBoardError, getLocalizedText("failed-delete-board", "Failed to delete board:") + " " + (err.message || getLocalizedText("unknown-error", "Unknown error")));
                    }
                })
                .finally(function() {
                    btnConfirmDeleteBoard.disabled = false;
                    btnConfirmDeleteBoard.textContent = getLocalizedText("delete-board", "Delete Board");
                });
            });
        }

        document.querySelectorAll(".kanban-column").forEach(function(col) {
            var status = col.dataset.columnStatus || "0";
            var sel = col.querySelector(".column-status-select");
            if (sel) {
                sel.value = status;
            }
        });

        function escapeHtml(str) {
            var div = document.createElement("div");
            div.textContent = str || "";
            return div.innerHTML;
        }

        function escapeAttribute(str) {
            return escapeHtml(str || "").replace(/"/g, "&quot;");
        }

        function renderDescriptionHtml(description) {
            if (!description) return "";
            var parts = description.split(/(!\[.*?\]\([^)]+\))/g);
            var html = parts.map(function(part) {
                var match = part.match(/^!\[(.*?)\]\(([^)]+)\)$/);
                if (match) {
                    var alt = escapeAttribute(match[1] || 'image');
                    var src = escapeAttribute(match[2]);
                    return '<img src="' + src + '" alt="' + alt + '" class="card-inline-img" loading="lazy" data-fullscreen-src="' + src + '">';
                }
                return escapeHtml(part);
            }).join("");
            return html.replace(/\n/g, '<br>');
        }

        function createMathPlaceholder(mathItems, tex, displayMode) {
            var token = "KANBAN_MATH_" + mathItems.length + "_TOKEN";
            mathItems.push({
                token: token,
                tex: tex,
                displayMode: displayMode
            });
            return token;
        }

        function protectInlineMath(line, mathItems) {
            var result = "";
            var index = 0;

            while (index < line.length) {
                var start = line.indexOf("$", index);
                if (start < 0) {
                    result += line.substring(index);
                    break;
                }

                var nextChar = line[start + 1] || "";
                if ((start > 0 && line[start - 1] === "\\") || nextChar === "$" || /\s|\d/.test(nextChar)) {
                    result += line.substring(index, start + 1);
                    index = start + 1;
                    continue;
                }

                var end = line.indexOf("$", start + 1);
                while (end > start && line[end - 1] === "\\") {
                    end = line.indexOf("$", end + 1);
                }

                if (end < 0 || /\s/.test(line[end - 1])) {
                    result += line.substring(index, start + 1);
                    index = start + 1;
                    continue;
                }

                result += line.substring(index, start);
                result += createMathPlaceholder(mathItems, line.substring(start + 1, end), false);
                index = end + 1;
            }

            return result;
        }

        function protectMathInMarkdown(description, mathItems) {
            var lines = description.split(/\r?\n/);
            var inFence = false;

            return lines.map(function(line) {
                if (/^\s*(```|~~~)/.test(line)) {
                    inFence = !inFence;
                    return line;
                }
                if (inFence) return line;

                var protectedLine = line.replace(/\$\$([\s\S]*?)\$\$/g, function(_, tex) {
                    return createMathPlaceholder(mathItems, tex, true);
                });
                return protectInlineMath(protectedLine, mathItems);
            }).join("\n");
        }

        function renderSafeMarkdownHtml(description, mathItems) {
            if (!description) return "";
            if (!window.marked || !window.DOMPurify) {
                return renderDescriptionHtml(description);
            }

            marked.setOptions({
                breaks: true,
                gfm: true
            });

            var protectedDescription = protectMathInMarkdown(description, mathItems);
            var rawHtml = marked.parse(protectedDescription);
            return DOMPurify.sanitize(rawHtml, {
                ADD_ATTR: ["target"],
                FORBID_TAGS: ["script", "style"],
                FORBID_ATTR: ["style"]
            });
        }

        function renderMathPlaceholders(container, mathItems) {
            if (!window.katex || mathItems.length === 0) return;

            var walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
            var textNodes = [];
            while (walker.nextNode()) {
                textNodes.push(walker.currentNode);
            }

            textNodes.forEach(function(textNode) {
                var value = textNode.nodeValue || "";
                var matchedItems = mathItems.filter(function(item) {
                    return value.indexOf(item.token) >= 0;
                });
                if (matchedItems.length === 0) return;

                var fragment = document.createDocumentFragment();
                var remaining = value;
                matchedItems.forEach(function(item) {
                    var tokenIndex = remaining.indexOf(item.token);
                    if (tokenIndex < 0) return;

                    fragment.appendChild(document.createTextNode(remaining.substring(0, tokenIndex)));
                    var mathNode = document.createElement(item.displayMode ? "div" : "span");
                    katex.render(item.tex, mathNode, {
                        displayMode: item.displayMode,
                        throwOnError: false
                    });
                    fragment.appendChild(mathNode);
                    remaining = remaining.substring(tokenIndex + item.token.length);
                });
                fragment.appendChild(document.createTextNode(remaining));
                textNode.replaceWith(fragment);
            });
        }

        function normalizeMermaidSource(source) {
            return (source || "").replace(/\{([^}"\n]*\?[^}"\n]*)\}/g, function(_, label) {
                return '{"' + label.replace(/"/g, '\\"') + '"}';
            });
        }

        function restoreMermaidCodeBlock(node, source) {
            var pre = document.createElement("pre");
            var code = document.createElement("code");
            code.className = "language-mermaid";
            code.textContent = source || "";
            pre.appendChild(code);
            node.replaceWith(pre);
        }

        function renderMermaidInDescription(container) {
            if (!window.mermaid) return;

            container.querySelectorAll("pre > code.language-mermaid, pre > code.lang-mermaid").forEach(function(code) {
                var pre = code.closest("pre");
                if (!pre) return;

                var diagram = document.createElement("div");
                var source = code.textContent || "";
                var renderSource = normalizeMermaidSource(source);
                var renderId = "card-description-mermaid-" + Date.now() + "-" + Math.random().toString(16).slice(2);
                diagram.className = "mermaid";
                diagram.textContent = source;
                pre.replaceWith(diagram);

                mermaid.render(renderId, renderSource).then(function(result) {
                    diagram.innerHTML = result.svg;
                    if (result.bindFunctions) {
                        result.bindFunctions(diagram);
                    }
                }).catch(function(err) {
                    console.error("Mermaid render failed:", err);
                    restoreMermaidCodeBlock(diagram, source);
                });
            });
        }

        function renderDescriptionPreview(description, container) {
            var mathItems = [];
            var html = renderSafeMarkdownHtml(description, mathItems);
            container.innerHTML = html;
            if (!html) return false;

            renderMathPlaceholders(container, mathItems);
            renderMermaidInDescription(container);
            container.querySelectorAll("a[href]").forEach(function(link) {
                link.rel = "noopener noreferrer";
            });
            return true;
        }

        var imageFullscreenOverlay = document.getElementById("imageFullscreenOverlay");
        var imageFullscreenImg = document.getElementById("imageFullscreenImg");

        if (imageFullscreenOverlay) {
            imageFullscreenOverlay.addEventListener("click", function() {
                closeImageFullscreen();
            });
            document.addEventListener("keydown", function(e) {
                if (e.key === "Escape" && imageFullscreenOverlay.classList.contains("active")) {
                    closeImageFullscreen();
                }
            });
        }
        function openImageFullscreen(src) {
            if (!imageFullscreenOverlay || !imageFullscreenImg) return;
            imageFullscreenImg.src = src;
            imageFullscreenOverlay.classList.add("active");
        }
        function closeImageFullscreen() {
            if (imageFullscreenOverlay) {
                imageFullscreenOverlay.classList.remove("active");
            }
        }

        var editCardDescriptionPreview = null;
        function updateDescriptionPreview() {
            if (!editCardDescriptionPreview) {
                editCardDescriptionPreview = document.getElementById("editCardDescriptionPreview");
            }
            if (!editCardDescriptionPreview) return;
            var desc = editCardDescription ? editCardDescription.value : "";
            if (renderDescriptionPreview(desc, editCardDescriptionPreview)) {
                editCardDescriptionPreview.style.display = "";
            } else {
                editCardDescriptionPreview.innerHTML = "";
                editCardDescriptionPreview.style.display = "none";
            }
        }

        function parseCardLabels(cardEl) {
            try {
                return JSON.parse(cardEl.dataset.labels || "[]");
            } catch (e) {
                console.error("Failed to parse labels", e);
                return [];
            }
        }

        function setCardLabels(cardEl, labels) {
            cardEl.dataset.labels = JSON.stringify(labels || []);
        }

        function getPriorityInfo(priorityValue) {
            switch (parseInt(priorityValue || "4", 10)) {
                case 0: return { text: getLocalizedText("urgent", "Urgent"), className: "priority-urgent" };
                case 1: return { text: getLocalizedText("high", "High"), className: "priority-high" };
                case 2: return { text: getLocalizedText("medium", "Medium"), className: "priority-medium" };
                case 3: return { text: getLocalizedText("low", "Low"), className: "priority-low" };
                default: return null;
            }
        }

        function formatDateTimeLocal(isoStr) {
            if (!isoStr) return "—";
            try {
                var d = /Z|[+-]\d{2}:?\d{2}$/.test(isoStr) ? new Date(isoStr) : new Date(isoStr + "Z");
                return isNaN(d.getTime()) ? isoStr : d.toLocaleString();
            } catch (e) {
                return isoStr;
            }
        }

        function parseCommentDate(utcStr) {
            if (!utcStr) return null;
            var d = /Z|[+-]\d{2}:?\d{2}$/.test(utcStr) ? new Date(utcStr) : new Date(utcStr + "Z");
            return isNaN(d.getTime()) ? null : d;
        }

        function formatCommentFullTime(utcStr) {
            var d = parseCommentDate(utcStr);
            if (!d) return utcStr || "";
            return d.toLocaleString();
        }

        function formatCommentTime(utcStr) {
            if (!utcStr) return "";
            var d = parseCommentDate(utcStr);
            if (!d) return utcStr;
            var now = new Date();
            var diffMs = now - d;
            var diffMin = Math.floor(diffMs / 60000);
            if (diffMin < 1) return getLocalizedText("just-now", "just now");
            if (diffMin < 60) return diffMin + getLocalizedText("m-ago", "m ago");
            var diffHr = Math.floor(diffMin / 60);
            if (diffHr < 24) return diffHr + getLocalizedText("h-ago", "h ago");
            var diffDays = Math.floor(diffHr / 24);
            if (diffDays < 7) return diffDays + getLocalizedText("d-ago", "d ago");
            return d.toLocaleString();
        }

        function renderMarkdownContent(text) {
            if (!text) return "";
            var result = escapeHtml(text);
            var quoteMatch = result.match(/^&gt;\s*([\s\S]*?)(?:\n|$)/);
            if (quoteMatch) {
                var quoted = quoteMatch[1];
                var afterQuote = result.substring(quoteMatch[0].length);
                var blockquote = '<blockquote>' + quoted + '</blockquote>';
                return blockquote + afterQuote;
            }
            return result;
        }

        function loadComments(cardId) {
            if (!commentsList) return;
            fetch("/Kanban/GetComments?cardId=" + cardId)
                .then(function(r) { return r.ok ? r.json() : []; })
                .then(function(comments) {
                    if (comments.length === 0) {
                        commentsList.innerHTML = '<div class="comment-empty-hint">' + getLocalizedText("no-comments-yet", "No comments yet.") + '</div>';
                        return;
                    }
                    commentsList.innerHTML = comments.map(function(c) {
                        var deleteBtn = '';
                        if (canEditCurrentBoard) {
                            deleteBtn = '<button type="button" class="comment-delete-btn" title="' + getLocalizedText("delete-comment", "Delete") + '">'
                                + '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 6h18"/><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6"/><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2"/></svg>'
                                + '</button>';
                        }

						let avatarUrl;
						if (c.Avatar) {
							avatarUrl = '<img src="'+
								c.Avatar
							+'" class="img-fluid rounded-circle me-1 mt-n2 mb-n2" alt="Super Administrator" style="width: 32px; height: 32px;">';
						} else {
								avatarUrl = c.AuthorInitial
						}

                        return '<div class="comment-item" data-comment-id="' + c.Id + '">'
							+ '<div class="card-assignee-avatar">' +
								avatarUrl
							+ '</div>'
                            + '<div class="comment-body">'
                                + '<div class="comment-header">'
                                    + '<span class="comment-author">' + escapeHtml(c.AuthorName || "Unknown") + '</span>'
                                    + '<span class="comment-time" title="' + escapeHtml(formatCommentFullTime(c.CreationTime)) + '">' + formatCommentTime(c.CreationTime) + '</span>'
                                + '</div>'
                                + '<div class="comment-text">' + renderMarkdownContent(c.Content) + '</div>'
                            + '</div>'
                            + deleteBtn
                        + '</div>';
                    }).join("");

                    // Attach delete handlers
                    commentsList.querySelectorAll('.comment-delete-btn').forEach(function(btn) {
                        btn.addEventListener('click', function() {
                            var commentId = this.closest('.comment-item').dataset.commentId;
                            deleteComment(commentId);
                        });
                    });
                })
                .catch(function(err) { console.error("GetComments failed:", err); });
        }

        function renderCardContent(cardEl) {
            if (!cardEl) return;

            var html = "";
            var topRow = [];
            var priorityInfo = getPriorityInfo(cardEl.dataset.priority);
            if (priorityInfo) {
                topRow.push('<span class="priority-badge ' + priorityInfo.className + '">' + priorityInfo.text + '</span>');
            }

            var assignedUserInitial = cardEl.dataset.assignedUserInitial || "";
            var assignedUserName = cardEl.dataset.assignedUserName || "";
            var assignedUserAvatarUrl = cardEl.dataset.assignedUserAvatarUrl || "";
            if (assignedUserInitial) {
                if (assignedUserAvatarUrl) {
                    topRow.push('<span class="card-assignee-avatar" title="' + escapeAttribute(assignedUserName || assignedUserInitial) + '"><img class="card-assignee-avatar-image" src="' + escapeAttribute(assignedUserAvatarUrl) + '" alt="' + escapeAttribute(assignedUserName || assignedUserInitial) + '" onerror="this.classList.add(\'d-none\');this.nextElementSibling.classList.remove(\'d-none\');"><span class="card-assignee-avatar-initial d-none">' + escapeHtml(assignedUserInitial) + '</span></span>');
                } else {
                    topRow.push('<span class="card-assignee-avatar" title="' + escapeAttribute(assignedUserName || assignedUserInitial) + '">' + escapeHtml(assignedUserInitial) + '</span>');
                }
            }

            if (topRow.length > 0) {
                html += '<div class="card-top-row">' + topRow.join("") + '</div>';
            }

            html += '<div class="card-title-text">' + escapeHtml(cardEl.dataset.title || "") + '</div>';

            var description = cardEl.dataset.description || "";
            if (description) {
                html += '<div class="card-description">' + renderDescriptionHtml(description) + '</div>';
            }

            var labels = parseCardLabels(cardEl);
            if (labels.length > 0) {
                html += '<div class="card-labels">' + labels.map(function(label) {
                    return '<span class="card-label-chip" style="background-color:' + escapeAttribute(label.Color) + '22;border-color:' + escapeAttribute(label.Color) + ';color:' + escapeAttribute(label.Color) + ';">' + escapeHtml(label.Name) + '</span>';
                }).join("") + '</div>';
            }

            var dueDate = cardEl.dataset.dueDate || "";
            if (dueDate) {
                var due = new Date(dueDate + "T00:00:00Z");
                var dueUtc = new Date(Date.UTC(due.getUTCFullYear(), due.getUTCMonth(), due.getUTCDate()));
                var column = cardEl.closest(".kanban-column");
                var isCompleted = column && column.dataset.columnStatus === "2";
                var isOverdue = !isCompleted && dueUtc.getTime() < Date.now();
                var formattedDue = (due.getUTCMonth() + 1).toString().padStart(2, "0") + "/" + due.getUTCDate().toString().padStart(2, "0");
                html += '<div class="card-due-date' + (isOverdue ? ' overdue' : '') + '"><i data-lucide="calendar" style="width:11px;height:11px"></i>' + formattedDue + '</div>';
            }

            cardEl.innerHTML = html;
            lucide.createIcons({ nodes: [cardEl] });
        }

        function bindCardClick(cardEl) {
            if (!cardEl || cardEl.dataset.clickBound === "1") return;
            cardEl.dataset.clickBound = "1";
            cardEl.addEventListener("click", function(e) {
                if (!isDragging && !e.defaultPrevented) {
                    openCardEditModal(cardEl);
                }
            });
        }

        var mobileColumnTrack = document.getElementById("mobileColumnTrack");
        var btnMobilePrevColumn = document.getElementById("btnMobilePrevColumn");
        var btnMobileNextColumn = document.getElementById("btnMobileNextColumn");
        var activeMobileColumnIndex = 0;
        var mobileScrollTimer = 0;

        function getKanbanColumns() {
            return Array.from(document.querySelectorAll(".kanban-column"));
        }

        function getColumnTitle(columnEl) {
            var titleEl = columnEl ? columnEl.querySelector(".column-title") : null;
            return titleEl ? titleEl.textContent.trim() : "";
        }

        function scrollToMobileColumn(index) {
            var columns = getKanbanColumns();
            if (columns.length === 0) return;
            var nextIndex = Math.max(0, Math.min(index, columns.length - 1));
            columns[nextIndex].scrollIntoView({ behavior: "smooth", inline: "start", block: "nearest" });
            updateMobileColumnState(nextIndex);
        }

        function updateMobileColumnState(index) {
            var columns = getKanbanColumns();
            if (columns.length === 0) return;

            activeMobileColumnIndex = Math.max(0, Math.min(index, columns.length - 1));
            if (btnMobilePrevColumn) {
                btnMobilePrevColumn.disabled = activeMobileColumnIndex === 0;
            }
            if (btnMobileNextColumn) {
                btnMobileNextColumn.disabled = activeMobileColumnIndex === columns.length - 1;
            }
            if (!mobileColumnTrack) return;

            mobileColumnTrack.querySelectorAll(".mobile-column-pill").forEach(function(pill) {
                var pillIndex = parseInt(pill.dataset.columnIndex, 10);
                pill.classList.toggle("active", pillIndex === activeMobileColumnIndex);
                if (pillIndex === activeMobileColumnIndex) {
                    pill.scrollIntoView({ behavior: "smooth", inline: "center", block: "nearest" });
                }
            });
        }

        function updateMobileColumnSwitcher() {
            if (!mobileColumnTrack) return;

            var columns = getKanbanColumns();
            mobileColumnTrack.innerHTML = "";
            columns.forEach(function(column, index) {
                var pill = document.createElement("button");
                pill.type = "button";
                pill.className = "mobile-column-pill";
                pill.dataset.columnIndex = index;
                pill.textContent = getColumnTitle(column) || (getLocalizedText("column", "column") + " " + (index + 1));
                pill.addEventListener("click", function() {
                    scrollToMobileColumn(index);
                });
                mobileColumnTrack.appendChild(pill);
            });
            updateMobileColumnState(Math.min(activeMobileColumnIndex, Math.max(columns.length - 1, 0)));
        }

        var kanbanContainer = document.getElementById("kanban-container");
        if (kanbanContainer) {
            kanbanContainer.addEventListener("scroll", function() {
                clearTimeout(mobileScrollTimer);
                mobileScrollTimer = setTimeout(function() {
                    var columns = getKanbanColumns();
                    if (columns.length === 0) return;

                    var containerLeft = kanbanContainer.getBoundingClientRect().left;
                    var closestIndex = 0;
                    var closestDistance = Number.MAX_VALUE;
                    columns.forEach(function(column, index) {
                        var distance = Math.abs(column.getBoundingClientRect().left - containerLeft);
                        if (distance < closestDistance) {
                            closestDistance = distance;
                            closestIndex = index;
                        }
                    });
                    updateMobileColumnState(closestIndex);
                }, 80);
            });
        }
        if (btnMobilePrevColumn) {
            btnMobilePrevColumn.addEventListener("click", function() {
                scrollToMobileColumn(activeMobileColumnIndex - 1);
            });
        }
        if (btnMobileNextColumn) {
            btnMobileNextColumn.addEventListener("click", function() {
                scrollToMobileColumn(activeMobileColumnIndex + 1);
            });
        }

        function refreshColumnCounts() {
            document.querySelectorAll(".kanban-column").forEach(function(col) {
                var count = col.querySelectorAll(".kanban-card").length;
                var badge = col.querySelector(".column-count");
                if (badge) {
                    badge.textContent = count;
                }

                var delBtn = col.querySelector(".btn-delete-column");
                if (delBtn) {
                    delBtn.classList.toggle("can-delete", canEditCurrentBoard);
                    delBtn.disabled = !canEditCurrentBoard;
                    delBtn.dataset.cardsCount = count;
                }

                var placeholder = col.querySelector(".column-empty-placeholder");
                if (placeholder) {
                    placeholder.style.display = count === 0 ? "" : "none";
                }
            });
            updateMobileColumnSwitcher();
        }

        document.querySelectorAll(".kanban-card").forEach(function(card) {
            renderCardContent(card);
            bindCardClick(card);
        });
        refreshColumnCounts();

        function handleCardMoved(evt) {
            var cardId = parseInt(evt.item.dataset.cardId, 10);
            var targetColumn = evt.to.closest(".kanban-column");
            var targetColumnId = parseInt(targetColumn.dataset.columnId, 10);
            var newOrder = evt.newIndex;

            fetch("/Kanban/MoveCard", {
                method: "POST",
                headers: {
                    "Content-Type": "application/x-www-form-urlencoded",
                    "RequestVerificationToken": csrfToken
                },
                body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken) + "&cardId=" + cardId + "&targetColumnId=" + targetColumnId + "&newOrder=" + newOrder
            })
            .then(function(r) { return r.ok ? r.json() : Promise.reject(r.status); })
            .then(function(data) {
                refreshColumnCounts();
                var cardEl = evt.item;
                cardEl.dataset.actualStart = data.ActualStartTime || "";
                cardEl.dataset.actualEnd = data.ActualEndTime || "";
                renderCardContent(cardEl);
            })
            .catch(function(err) {
                console.error("MoveCard failed:", err);
            })
            .finally(function() {
                setTimeout(function() { isDragging = false; }, 100);
            });
        }

        if (canEditCurrentBoard) {
            document.querySelectorAll(".column-cards").forEach(function(column) {
                new Sortable(column, {
                    group: "kanban-cards",
                    draggable: ".kanban-card",
                    animation: 200,
                    easing: "cubic-bezier(0.25, 0.46, 0.45, 0.94)",
                    ghostClass: "sortable-ghost",
                    chosenClass: "sortable-chosen",
                    onStart: function() { isDragging = true; },
                    onEnd: handleCardMoved
                });
            });

            var container = document.getElementById("kanban-container");
            if (container) {
                new Sortable(container, {
                    animation: 200,
                    easing: "cubic-bezier(0.25, 0.46, 0.45, 0.94)",
                    handle: ".column-header",
                    onEnd: function(evt) {
                        var columnId = parseInt(evt.item.dataset.columnId, 10);
                        var newOrder = evt.newIndex;

                        fetch("/Kanban/MoveColumn", {
                            method: "POST",
                            headers: {
                                "Content-Type": "application/x-www-form-urlencoded",
                                "RequestVerificationToken": csrfToken
                            },
                            body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken) + "&columnId=" + columnId + "&newOrder=" + newOrder
                        }).catch(function(err) {
                            console.error("MoveColumn failed:", err);
                        });
                        updateMobileColumnSwitcher();
                    }
                });
            }
        }

        var addColumnModal = document.getElementById("addColumnModal");
        var btnAddColumn = document.getElementById("btnAddColumn");
        var columnNameInput = document.getElementById("columnNameInput");
        var columnError = document.getElementById("columnError");
        var btnSaveColumn = document.getElementById("btnSaveColumn");

        if (canEditCurrentBoard && btnAddColumn && addColumnModal && columnNameInput && columnError) {
            btnAddColumn.addEventListener("click", function() {
                columnNameInput.value = "";
                columnNameInput.classList.remove("is-invalid");
                columnError.textContent = "";
                new bootstrap.Modal(addColumnModal).show();
            });
        }

        function bindColumnTitleEdit(editBtn) {
            if (!editBtn || editBtn.dataset.editBound === "1") return;
            editBtn.dataset.editBound = "1";
            editBtn.addEventListener("click", function(e) {
                e.stopPropagation();
                var columnId = this.dataset.columnId;
                var headerLeft = this.closest(".column-header-left");
                var titleSpan = headerLeft.querySelector(".column-title[data-column-id='" + columnId + "']");
                if (!titleSpan || titleSpan.querySelector("input")) return;

                var currentName = titleSpan.textContent;
                var input = document.createElement("input");
                input.type = "text";
                input.className = "column-title-input";
                input.value = currentName;
                input.dataset.columnId = columnId;
                input.dataset.originalName = currentName;

                titleSpan.style.display = "none";
                titleSpan.parentNode.insertBefore(input, titleSpan.nextSibling);

                input.focus();
                input.select();

                function saveRename() {
                    var newName = input.value.trim();
                    if (!newName || newName === input.dataset.originalName) {
                        cancelRename();
                        return;
                    }

                    fetch("/Kanban/RenameColumn", {
                        method: "POST",
                        headers: {
                            "Content-Type": "application/x-www-form-urlencoded",
                            "RequestVerificationToken": csrfToken
                        },
                        body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken)
                            + "&columnId=" + columnId
                            + "&name=" + encodeURIComponent(newName)
                    })
                    .then(function(r) {
                        if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("server-error", "Server error") + " " + r.status); });
                        return r.json();
                    })
                    .then(function() {
                        titleSpan.textContent = newName;
                        titleSpan.style.display = "";
                        input.remove();
                        updateMobileColumnSwitcher();
                    })
                    .catch(function(err) {
                        showFriendlyDialog(getLocalizedText("failed-rename-column", "Failed to rename column:") + " " + (err.message || getLocalizedText("unknown-error", "Unknown error")), getLocalizedText("error", "Error"));
                        cancelRename();
                    });
                }

                function cancelRename() {
                    titleSpan.style.display = "";
                    input.remove();
                }

                input.addEventListener("keydown", function(ev) {
                    if (ev.key === "Enter") {
                        ev.preventDefault();
                        saveRename();
                    } else if (ev.key === "Escape") {
                        ev.preventDefault();
                        cancelRename();
                    }
                });

                input.addEventListener("blur", function() {
                    saveRename();
                });
            });
        }

        document.querySelectorAll(".btn-edit-column-title").forEach(function(btn) {
            bindColumnTitleEdit(btn);
        });

        function addColumnToDom(columnId, columnName, columnStatus) {
            var container = document.getElementById("kanban-container");
            if (!container) return;

            var dotColors = ["dot-blue", "dot-orange", "dot-green", "dot-purple", "dot-pink", "dot-teal", "dot-amber", "dot-indigo"];
            var existingCols = container.querySelectorAll(".kanban-column");
            var dotClass = dotColors[existingCols.length % dotColors.length];
            var status = columnStatus || 0;

            var col = document.createElement("div");
            col.className = "kanban-column";
            col.dataset.columnId = columnId;
            col.dataset.columnStatus = status;
            col.innerHTML =
                '<div class="column-header">' +
                    '<div class="column-header-left">' +
                        '<span class="column-dot ' + dotClass + '"></span>' +
                        '<span class="column-title" data-column-id="' + columnId + '">' + escapeHtml(columnName) + '</span>' +
                        '<span class="column-count">0</span>' +
                        '<button class="btn-edit-column-title" data-column-id="' + columnId + '" title="' + getLocalizedText("rename-column", "Rename column") + '">' +
                            '<i class="align-middle" style="width:12px;height:12px" data-lucide="pencil"></i>' +
                        '</button>' +
                    '</div>' +
                    '<div class="d-flex align-items-center gap-1">' +
                        '<select class="column-status-select" data-column-id="' + columnId + '">' +
                            '<option value="0" selected>' + getLocalizedText("not-started", "Not Started") + '</option>' +
                            '<option value="1">' + getLocalizedText("in-progress", "In Progress") + '</option>' +
                            '<option value="2">' + getLocalizedText("completed", "Completed") + '</option>' +
                        '</select>' +
                        '<button class="btn-delete-column can-delete" data-column-id="' + columnId + '" title="' + getLocalizedText("delete-empty-column", "Delete empty column") + '">' +
                            '<i class="align-middle" style="width:14px;height:14px" data-lucide="trash-2"></i>' +
                        '</button>' +
                    '</div>' +
                '</div>' +
                '<div class="column-cards" data-column-id="' + columnId + '">' +
                    '<div class="column-empty-placeholder">' + getLocalizedText("drop-cards-here", "Drop cards here") + '</div>' +
                '</div>' +
                '<button class="btn-add-card" data-column-id="' + columnId + '">' +
                    '<i class="align-middle" style="width:14px;height:14px" data-lucide="plus"></i> ' + getLocalizedText("add-card", "Add Card") +
                '</button>';

            var statusSel = col.querySelector(".column-status-select");
            statusSel.addEventListener("change", function() { handleColumnStatusChange(this); });

            var cardList = col.querySelector(".column-cards");
            new Sortable(cardList, {
                group: "kanban-cards",
                draggable: ".kanban-card",
                animation: 200,
                easing: "cubic-bezier(0.25, 0.46, 0.45, 0.94)",
                ghostClass: "sortable-ghost",
                chosenClass: "sortable-chosen",
                onStart: function() { isDragging = true; },
                onEnd: handleCardMoved
            });

            col.querySelector(".btn-add-card").addEventListener("click", function() {
                openAddCardModal(columnId);
            });

            container.appendChild(col);
            lucide.createIcons({ nodes: [col] });

            var newEditBtn = col.querySelector(".btn-edit-column-title");
            if (newEditBtn) bindColumnTitleEdit(newEditBtn);
            updateMobileColumnSwitcher();
        }

        if (canEditCurrentBoard && btnSaveColumn && columnNameInput && columnError) {
            btnSaveColumn.addEventListener("click", function() {
                var name = columnNameInput.value.trim();
                if (!name) {
                    columnNameInput.classList.add("is-invalid");
                    columnError.textContent = getLocalizedText("column-name-required", "Column name is required.");
                    return;
                }

                btnSaveColumn.disabled = true;
                btnSaveColumn.textContent = getLocalizedText("saving", "Saving...");

                fetch("/Kanban/CreateColumn", {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/x-www-form-urlencoded",
                        "RequestVerificationToken": csrfToken
                    },
                    body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken) + "&boardId=" + currentBoardId + "&name=" + encodeURIComponent(name)
                })
                .then(function(r) {
                    if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("server-error", "Server error") + " " + r.status); });
                    return r.json();
                })
                .then(function(data) {
                    addColumnToDom(data.Id, data.Name, data.ColumnStatus);
                    bootstrap.Modal.getInstance(addColumnModal).hide();
                })
                .catch(function(err) {
                    console.error("CreateColumn failed:", err);
                    columnNameInput.classList.add("is-invalid");
                    columnError.textContent = err.message || getLocalizedText("failed-create-column", "Failed to create column.");
                })
                .finally(function() {
                    btnSaveColumn.disabled = false;
                    btnSaveColumn.textContent = getLocalizedText("add-column", "Add Column");
                });
            });
        }

        var addCardModal = document.getElementById("addCardModal");
        var cardTitleInput = document.getElementById("cardTitleInput");
        var cardDescInput = document.getElementById("cardDescInput");
        var cardError = document.getElementById("cardError");
        var btnSaveCard = document.getElementById("btnSaveCard");
        var currentColumnId = 0;

        async function uploadPastedImage(textarea, blob) {
            var uploadingText = '[Uploading image...]';
            var cursorPos = textarea.selectionStart;
            var textBefore = textarea.value.substring(0, cursorPos);
            var textAfter = textarea.value.substring(cursorPos);
            textarea.value = textBefore + uploadingText + textAfter;
            textarea.selectionStart = cursorPos;
            textarea.selectionEnd = cursorPos + uploadingText.length;

            try {
                var formData = new FormData();
                var ext = blob.type === 'image/png' ? 'png' :
                          blob.type === 'image/gif' ? 'gif' :
                          blob.type === 'image/webp' ? 'webp' : 'jpg';
                formData.append('file', blob, 'paste-' + Date.now() + '.' + ext);

                var uploadResp = await fetch(kanbanImageUploadUrl, {
                    method: 'POST',
                    body: formData
                });
                if (!uploadResp.ok) {
                    var errText = await uploadResp.text();
                    throw new Error(errText || 'Upload failed. Status: ' + uploadResp.status);
                }
                var data = await uploadResp.json();

                var imageMarkdown = '\n![image](' + data.InternetPath + ')\n';
                textarea.value = textarea.value.replace(uploadingText, imageMarkdown);
                textarea.dispatchEvent(new Event('input', { bubbles: true }));
                var newCursor = textarea.value.indexOf(imageMarkdown) + imageMarkdown.length;
                textarea.selectionStart = newCursor;
                textarea.selectionEnd = newCursor;
            } catch (err) {
                textarea.value = textarea.value.replace(uploadingText, '');
                console.error('Image upload failed:', err);
                showFriendlyDialog(getLocalizedText("failed-upload-pasted-image", "Failed to upload pasted image:") + " " + (err.message || getLocalizedText("unknown-error", "Unknown error")), getLocalizedText("error", "Error"));
            }
        }

        function handlePasteInTextarea(textarea, e) {
            var items = (e.clipboardData || e.originalEvent.clipboardData).items;
            for (var i = 0; i < items.length; i++) {
                if (items[i].type.indexOf('image') === 0) {
                    e.preventDefault();
                    e.stopPropagation();
                    uploadPastedImage(textarea, items[i].getAsFile());
                    break;
                }
            }
        }

        if (canEditCurrentBoard && cardDescInput) {
            cardDescInput.addEventListener('paste', function(e) { handlePasteInTextarea(cardDescInput, e); });
        }

        function openAddCardModal(columnId) {
            if (!addCardModal || !cardTitleInput || !cardDescInput || !cardError) return;
            currentColumnId = columnId;
            cardTitleInput.value = "";
            cardDescInput.value = "";
            cardTitleInput.classList.remove("is-invalid");
            cardError.textContent = "";
            new bootstrap.Modal(addCardModal).show();
        }

        if (canEditCurrentBoard) {
            document.querySelectorAll(".btn-add-card").forEach(function(btn) {
                btn.addEventListener("click", function() {
                    openAddCardModal(parseInt(this.dataset.columnId, 10));
                });
            });
        }

        function addCardToDom(cardId, title, description, columnId, creationTime, creatorInitial, creatorAvatarUrl, creatorUserName) {
            var cardList = document.querySelector('.column-cards[data-column-id="' + columnId + '"]');
            if (!cardList) {
                console.error("addCardToDom: column-cards not found for columnId=" + columnId);
                return;
            }

            var placeholder = cardList.querySelector(".column-empty-placeholder");
            if (placeholder) placeholder.style.display = "none";

            var card = document.createElement("div");
            card.className = "kanban-card can-drag";
            card.dataset.cardId = cardId;
            card.dataset.title = title;
            card.dataset.description = description || "";
            card.dataset.plannedStart = "";
            card.dataset.dueDate = "";
            card.dataset.actualStart = "";
            card.dataset.actualEnd = "";
            card.dataset.priority = "4";
            card.dataset.assignedUserId = "";
            card.dataset.assignedUserName = "";
            card.dataset.assignedUserInitial = "";
            card.dataset.assignedUserAvatarUrl = "";
            card.dataset.creatorUserName = creatorUserName || "";
            card.dataset.creatorUserInitial = creatorInitial || "";
            card.dataset.creatorUserAvatarUrl = creatorAvatarUrl || "";
            card.dataset.creationTime = creationTime || "";
            card.dataset.labels = "[]";
            renderCardContent(card);
            bindCardClick(card);
            cardList.appendChild(card);
            refreshColumnCounts();
        }

        if (canEditCurrentBoard && btnSaveCard && cardTitleInput && cardDescInput && cardError) {
            btnSaveCard.addEventListener("click", function() {
                var title = cardTitleInput.value.trim();
                var description = cardDescInput.value.trim();

                if (!title) {
                    cardTitleInput.classList.add("is-invalid");
                    cardError.textContent = getLocalizedText("title-required", "Title is required.");
                    return;
                }

                btnSaveCard.disabled = true;
                btnSaveCard.textContent = getLocalizedText("saving", "Saving...");

                var body = "__RequestVerificationToken=" + encodeURIComponent(csrfToken)
                    + "&columnId=" + currentColumnId
                    + "&title=" + encodeURIComponent(title)
                    + "&description=" + encodeURIComponent(description);

                fetch("/Kanban/CreateCard", {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/x-www-form-urlencoded",
                        "RequestVerificationToken": csrfToken
                    },
                    body: body
                })
                .then(function(r) {
                    if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("server-error", "Server error") + " " + r.status); });
                    return r.json();
                })
                .then(function(data) {
                    addCardToDom(data.Id, data.Title, data.Description, data.ColumnId,
                        data.CreationTime, data.CreatorUserInitial, data.CreatorUserAvatarUrl, data.CreatorUserName);
                    bootstrap.Modal.getInstance(addCardModal).hide();
                })
                .catch(function(err) {
                    console.error("CreateCard failed:", err);
                    cardTitleInput.classList.add("is-invalid");
                    cardError.textContent = err.message || getLocalizedText("failed-create-card", "Failed to create card.");
                })
                .finally(function() {
                    btnSaveCard.disabled = false;
                    btnSaveCard.textContent = getLocalizedText("add-card", "Add Card");
                });
            });
        }

        var deleteColumnConfirmModalEl = document.getElementById("deleteColumnConfirmModal");
        var deleteColumnConfirmModal = deleteColumnConfirmModalEl ? new bootstrap.Modal(deleteColumnConfirmModalEl) : null;
        var btnConfirmDeleteColumn = document.getElementById("btnConfirmDeleteColumn");
        var deleteColumnError = document.getElementById("deleteColumnError");
        var deleteColumnNameDisplay = document.getElementById("deleteColumnNameDisplay");
        var columnIdToDelete = null;
        var columnElToDelete = null;

        function deleteColumn(columnId, columnEl) {
            columnIdToDelete = columnId;
            columnElToDelete = columnEl;

            var columnNameSpan = columnEl.querySelector(".column-title");
            var columnName = columnNameSpan ? columnNameSpan.textContent : getLocalizedText("column", "column");

            if (deleteColumnNameDisplay) {
                deleteColumnNameDisplay.textContent = columnName;
            }
            if (deleteColumnError) {
                clearInlineAlert(deleteColumnError);
            }

            var delBtn = columnEl.querySelector(".btn-delete-column");
            var cardsCount = delBtn ? parseInt(delBtn.dataset.cardsCount, 10) || 0 : 0;
            var warningEl = document.getElementById("deleteColumnWarning");
            if (warningEl) {
                if (cardsCount > 0) {
                    showInlineAlert(warningEl, getLocalizedText("this-column-contains", "This column contains") + " " + cardsCount + " " + getLocalizedText("cards-delete-warning", "card(s). Deleting the column will also permanently delete all its cards."), "alert-triangle");
                } else {
                    clearInlineAlert(warningEl);
                }
            }

            if (btnConfirmDeleteColumn) {
                btnConfirmDeleteColumn.disabled = false;
                btnConfirmDeleteColumn.textContent = getLocalizedText("delete-column", "Delete Column");
            }
            if (deleteColumnConfirmModal) {
                deleteColumnConfirmModal.show();
            }
        }

        if (btnConfirmDeleteColumn) {
            btnConfirmDeleteColumn.addEventListener("click", function() {
                if (columnIdToDelete === null) return;

                btnConfirmDeleteColumn.disabled = true;
                btnConfirmDeleteColumn.textContent = getLocalizedText("deleting", "Deleting...");
                if (deleteColumnError) {
                    clearInlineAlert(deleteColumnError);
                }

                fetch("/Kanban/DeleteColumn", {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/x-www-form-urlencoded",
                        "RequestVerificationToken": csrfToken
                    },
                    body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken) + "&columnId=" + columnIdToDelete
                })
                .then(function(r) {
                    if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("server-error", "Server error") + " " + r.status); });
                    if (deleteColumnConfirmModal) {
                        deleteColumnConfirmModal.hide();
                    }
                    if (columnElToDelete) {
                        columnElToDelete.remove();
                        updateMobileColumnSwitcher();
                    }
                })
                .catch(function(err) {
                    console.error("DeleteColumn failed:", err);
                    if (deleteColumnError) {
                        showInlineAlert(deleteColumnError, getLocalizedText("failed-delete-column", "Failed to delete column:") + " " + (err.message || getLocalizedText("unknown-error", "Unknown error")));
                    }
                })
                .finally(function() {
                    btnConfirmDeleteColumn.disabled = false;
                    btnConfirmDeleteColumn.textContent = getLocalizedText("delete-column", "Delete Column");
                });
            });
        }

        if (canEditCurrentBoard) {
            document.addEventListener("click", function(e) {
                var btn = e.target.closest(".btn-delete-column");
                if (btn && btn.classList.contains("can-delete")) {
                    e.stopPropagation();
                    var columnId = parseInt(btn.dataset.columnId, 10);
                    var columnEl = btn.closest(".kanban-column");
                    deleteColumn(columnId, columnEl);
                }
            });
        }

        function handleColumnStatusChange(selectEl) {
            if (!canEditCurrentBoard) return;
            var columnId = parseInt(selectEl.dataset.columnId, 10);
            var status = parseInt(selectEl.value, 10);
            var colEl = selectEl.closest(".kanban-column");
            if (colEl) {
                colEl.dataset.columnStatus = status;
                colEl.querySelectorAll(".kanban-card").forEach(function(card) {
                    renderCardContent(card);
                });
            }
            fetch("/Kanban/UpdateColumnStatus", {
                method: "POST",
                headers: { "Content-Type": "application/x-www-form-urlencoded", "RequestVerificationToken": csrfToken },
                body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken) + "&columnId=" + columnId + "&status=" + status
            }).catch(function(err) { console.error("UpdateColumnStatus failed:", err); });
        }

        document.querySelectorAll(".column-status-select").forEach(function(sel) {
            sel.addEventListener("change", function() { handleColumnStatusChange(this); });
        });

        var currentEditCardId = 0;
        var currentEditCardElement = null;
        var currentEditLabels = [];
        var boardMembersLoaded = false;
        var editCardModal = document.getElementById("editCardModal");
        var editCardModalTitle = document.getElementById("editCardModalTitle");
        var editCardDetailsView = document.getElementById("editCardDetailsView");
        var editCardTitle = document.getElementById("editCardTitle");
        var editCardDescription = document.getElementById("editCardDescription");
        var editCardPlannedStart = document.getElementById("editCardPlannedStart");
        var editCardDueDate = document.getElementById("editCardDueDate");
        var editCardPriority = document.getElementById("editCardPriority");
        var editCardAssignee = document.getElementById("editCardAssignee");
        var editCardActualStart = document.getElementById("editCardActualStart");
        var editCardActualEnd = document.getElementById("editCardActualEnd");
        var editCardCreatorAvatar = document.getElementById("editCardCreatorAvatar");
        var editCardCreatorInitial = document.getElementById("editCardCreatorInitial");
        var editCardCreatorName = document.getElementById("editCardCreatorName");
        var editCardCreationTime = document.getElementById("editCardCreationTime");
        var editCardLabelInput = document.getElementById("editCardLabelInput");
        var editCardLabelSuggestions = document.getElementById("editCardLabelSuggestions");
        var editCardLabels = document.getElementById("editCardLabels");
        var btnAddLabel = document.getElementById("btnAddLabel");
        var btnUpdateCard = document.getElementById("btnUpdateCard");
        var btnMoveCard = document.getElementById("btnMoveCard");
        var btnTransferCard = document.getElementById("btnTransferCard");
        var btnDeleteCard = document.getElementById("btnDeleteCard");
        var moveCardView = document.getElementById("moveCardView");
        var btnBackFromMoveCard = document.getElementById("btnBackFromMoveCard");
        var moveTargetColumn = document.getElementById("moveTargetColumn");
        var moveCardError = document.getElementById("moveCardError");
        var btnConfirmMoveCard = document.getElementById("btnConfirmMoveCard");
        var deleteCardView = document.getElementById("deleteCardView");
        var btnBackFromDeleteCard = document.getElementById("btnBackFromDeleteCard");
        var btnConfirmDeleteCard = document.getElementById("btnConfirmDeleteCard");
        var deleteCardError = document.getElementById("deleteCardError");
        var transferCardView = document.getElementById("transferCardView");
        var btnBackFromTransferCard = document.getElementById("btnBackFromTransferCard");
        var transferTargetBoard = document.getElementById("transferTargetBoard");
        var transferTargetColumn = document.getElementById("transferTargetColumn");
        var transferCardError = document.getElementById("transferCardError");
        var btnConfirmTransferCard = document.getElementById("btnConfirmTransferCard");
        var transferTargets = [];
        var commentsList = document.getElementById("commentsList");
        var commentInput = document.getElementById("commentInput");
        var btnAddComment = document.getElementById("btnAddComment");
        var deleteCommentConfirmModalEl = document.getElementById("deleteCommentConfirmModal");
        var deleteCommentConfirmModal = deleteCommentConfirmModalEl ? new bootstrap.Modal(deleteCommentConfirmModalEl) : null;
        var btnConfirmDeleteComment = document.getElementById("btnConfirmDeleteComment");
        var deleteCommentError = document.getElementById("deleteCommentError");
        var commentIdToDelete = null;

        function showEditCardView(view) {
            if (!editCardDetailsView) return;

            editCardDetailsView.classList.toggle("d-none", view !== "details");
            if (moveCardView) {
                moveCardView.classList.toggle("d-none", view !== "move");
            }
            if (transferCardView) {
                transferCardView.classList.toggle("d-none", view !== "transfer");
            }
            if (deleteCardView) {
                deleteCardView.classList.toggle("d-none", view !== "delete");
            }
            if (!editCardModalTitle) return;

            editCardModalTitle.classList.toggle("text-danger", view === "delete");
            if (view === "move") {
                editCardModalTitle.textContent = getLocalizedText("move-card", "Move Card");
                return;
            }
            if (view === "transfer") {
                editCardModalTitle.textContent = getLocalizedText("transfer-card", "Transfer Card");
                return;
            }
            if (view === "delete") {
                editCardModalTitle.textContent = getLocalizedText("delete", "Delete");
                return;
            }
            editCardModalTitle.textContent = getLocalizedText("card-details", "Card Details");
        }

        function updateCardAssignment(cardEl, assignedUserId, assignedUserName, assignedUserInitial, assignedUserAvatarUrl) {
            cardEl.dataset.assignedUserId = assignedUserId || "";
            cardEl.dataset.assignedUserName = assignedUserName || "";
            cardEl.dataset.assignedUserInitial = assignedUserInitial || "";
            cardEl.dataset.assignedUserAvatarUrl = assignedUserAvatarUrl || "";
        }

        function syncCurrentCardLabels() {
            if (!currentEditCardElement) return;
            setCardLabels(currentEditCardElement, currentEditLabels);
            renderCardContent(currentEditCardElement);
            renderSelectedLabels();
        }

        function updateLabelColorAcrossBoard(labelId, color) {
            document.querySelectorAll(".kanban-card").forEach(function(card) {
                var labels = parseCardLabels(card);
                var updated = false;
                labels.forEach(function(label) {
                    if (label.Id === labelId) {
                        label.Color = color;
                        updated = true;
                    }
                });
                if (updated) {
                    setCardLabels(card, labels);
                    renderCardContent(card);
                }
            });
        }

        function renderSelectedLabels() {
            if (!editCardLabels) return;

            if (currentEditLabels.length === 0) {
                editCardLabels.innerHTML = '<div class="text-muted small">' + getLocalizedText("no-labels-yet", "No labels yet.") + '</div>';
                return;
            }

            editCardLabels.innerHTML = currentEditLabels.map(function(label) {
                var colorPicker = canEditCurrentBoard
                    ? '<input type="color" class="edit-label-color" value="' + escapeAttribute(label.Color) + '" data-label-color-id="' + label.Id + '" aria-label="' + getLocalizedText("change-color", "Change {0} color").replace("{0}", escapeAttribute(label.Name)) + '">'
                    : '';
                var removeButton = canEditCurrentBoard
                    ? '<button type="button" class="btn-remove-label" data-remove-label-id="' + label.Id + '" aria-label="' + getLocalizedText("remove", "Remove {0}").replace("{0}", escapeAttribute(label.Name)) + '">×</button>'
                    : '';
                return '<div class="edit-label-chip">'
                    + '<span class="edit-label-name" style="background-color:' + escapeAttribute(label.Color) + '22;border-color:' + escapeAttribute(label.Color) + ';color:' + escapeAttribute(label.Color) + ';">' + escapeHtml(label.Name) + '</span>'
                    + colorPicker
                    + removeButton
                    + '</div>';
            }).join('');

            if (!canEditCurrentBoard) return;

            editCardLabels.querySelectorAll("[data-remove-label-id]").forEach(function(button) {
                button.addEventListener("click", function() {
                    var labelId = parseInt(this.dataset.removeLabelId, 10);
                    fetch("/Kanban/RemoveLabel", {
                        method: "POST",
                        headers: { "Content-Type": "application/x-www-form-urlencoded", "RequestVerificationToken": csrfToken },
                        body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken) + "&cardId=" + currentEditCardId + "&labelId=" + labelId
                    })
                    .then(function(r) {
                        if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("failed-remove-label", "Failed to remove label.")); });
                        currentEditLabels = currentEditLabels.filter(function(label) { return label.Id !== labelId; });
                        syncCurrentCardLabels();
                    })
                    .catch(function(err) {
                        showFriendlyDialog(err.message || getLocalizedText("failed-remove-label", "Failed to remove label."), getLocalizedText("error", "Error"));
                    });
                });
            });

            editCardLabels.querySelectorAll("[data-label-color-id]").forEach(function(input) {
                input.addEventListener("change", function() {
                    var labelId = parseInt(this.dataset.labelColorId, 10);
                    var color = this.value;
                    fetch("/Kanban/UpdateLabelColor", {
                        method: "POST",
                        headers: { "Content-Type": "application/x-www-form-urlencoded", "RequestVerificationToken": csrfToken },
                        body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken) + "&cardId=" + currentEditCardId + "&labelId=" + labelId + "&color=" + encodeURIComponent(color)
                    })
                    .then(function(r) {
                        if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("failed-update-label-color", "Failed to update label color.")); });
                        return r.json();
                    })
                    .then(function(data) {
                        currentEditLabels.forEach(function(label) {
                            if (label.Id === data.Id) {
                                label.Color = data.Color;
                            }
                        });
                        updateLabelColorAcrossBoard(data.Id, data.Color);
                        syncCurrentCardLabels();
                    })
                    .catch(function(err) {
                        showFriendlyDialog(err.message || getLocalizedText("failed-update-label-color", "Failed to update label color."), getLocalizedText("error", "Error"));
                    });
                });
            });
        }

        function loadBoardMembers(selectedUserId) {
            if (!canEditCurrentBoard || !editCardAssignee) {
                return Promise.resolve();
            }

            if (boardMembersLoaded) {
                editCardAssignee.value = selectedUserId || "";
                return Promise.resolve();
            }

            editCardAssignee.innerHTML = '<option value="">' + getLocalizedText("unassigned", "Unassigned") + '</option>';
            return fetch("/Kanban/GetBoardMembers?boardId=" + currentBoardId)
                .then(function(r) {
                    if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("failed-load-members", "Failed to load board members.")); });
                    return r.json();
                })
                .then(function(members) {
                    members.forEach(function(member) {
                        var option = document.createElement("option");
                        option.value = member.Id;
                        option.textContent = member.DisplayName || member.UserName;
                        editCardAssignee.appendChild(option);
                    });
                    editCardAssignee.value = selectedUserId || "";
                    boardMembersLoaded = true;
                })
                .catch(function(err) {
                    console.error(err);
                });
        }

        function fetchLabelSuggestions(query) {
            if (!canEditCurrentBoard || !editCardLabelSuggestions) return;

            if (!query) {
                editCardLabelSuggestions.innerHTML = "";
                return;
            }

            fetch("/Kanban/SearchLabels?q=" + encodeURIComponent(query))
                .then(function(r) { return r.ok ? r.json() : []; })
                .then(function(labels) {
                    editCardLabelSuggestions.innerHTML = labels.map(function(label) {
                        return '<option value="' + escapeAttribute(label.Name) + '"></option>';
                    }).join('');
                })
                .catch(function(err) {
                    console.error("SearchLabels failed:", err);
                });
        }

        function submitLabelInput() {
            if (!canEditCurrentBoard || !editCardLabelInput) return;

            var labelName = editCardLabelInput.value.trim().replace(/,$/, "").trim();
            if (!labelName) return;

            var exists = currentEditLabels.some(function(label) {
                return (label.Name || "").toLowerCase() === labelName.toLowerCase();
            });
            if (exists) {
                editCardLabelInput.value = "";
                return;
            }

            fetch("/Kanban/AddLabel", {
                method: "POST",
                headers: { "Content-Type": "application/x-www-form-urlencoded", "RequestVerificationToken": csrfToken },
                body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken) + "&cardId=" + currentEditCardId + "&name=" + encodeURIComponent(labelName) + (typeof editLabelColorPicker !== 'undefined' && editLabelColorPicker ? "&color=" + encodeURIComponent(editLabelColorPicker.value) : "")
            })
            .then(function(r) {
                if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("failed-add-label", "Failed to add label.")); });
                return r.json();
            })
            .then(function(data) {
                currentEditLabels.push({ Id: data.Id, Name: data.Name, Color: data.Color });
                currentEditLabels.sort(function(a, b) { return (a.Name || "").localeCompare(b.Name || ""); });
                editCardLabelInput.value = "";
                syncCurrentCardLabels();
                fetchLabelSuggestions("");
            })
            .catch(function(err) {
                showFriendlyDialog(err.message || getLocalizedText("failed-add-label", "Failed to add label."), getLocalizedText("error", "Error"));
            });
        }

        if (editCardLabelInput) {
            editCardLabelInput.addEventListener("input", function() {
                clearTimeout(labelSearchTimeout);
                var query = this.value.trim();
                labelSearchTimeout = setTimeout(function() { fetchLabelSuggestions(query); }, 150);
            });
            editCardLabelInput.addEventListener("keydown", function(e) {
                if (e.key === "Enter" || e.key === ",") {
                    e.preventDefault();
                    submitLabelInput();
                }
            });
        }

        if (btnAddLabel) {
            btnAddLabel.addEventListener("click", submitLabelInput);
        }

        var editLabelColorPicker = document.getElementById("editLabelColorPicker");
        var labelColorPresetsContainer = document.getElementById("labelColorPresets");
        var presetColors = ["#EF4444", "#F97316", "#EAB308", "#22C55E", "#3B82F6", "#8B5CF6", "#EC4899", "#14B8A6"];
        if (labelColorPresetsContainer) {
            presetColors.forEach(function(c, index) {
                var btn = document.createElement("button");
                btn.type = "button";
                btn.className = "label-color-swatch";
                btn.title = c;
                btn.style.background = c;
                btn.addEventListener("click", function() {
                    if (editLabelColorPicker) editLabelColorPicker.value = c;
                    labelColorPresetsContainer.querySelectorAll('.label-color-swatch').forEach(function(b){ b.classList.remove('selected'); });
                    btn.classList.add('selected');
                });
                if (index === 0) {
                    btn.classList.add('selected');
                }
                labelColorPresetsContainer.appendChild(btn);
            });
        }
        if (editLabelColorPicker) {
            editLabelColorPicker.addEventListener('input', function() {
                if (labelColorPresetsContainer) labelColorPresetsContainer.querySelectorAll('button').forEach(function(b){ b.style.outline = ''; });
            });
        }

        if (editCardDescription && canEditCurrentBoard) {
            editCardDescription.addEventListener('paste', function(e) { handlePasteInTextarea(editCardDescription, e); });
        }
        function autoResizeTextarea(textarea) {
            textarea.style.height = 'auto';
            textarea.style.height = Math.min(textarea.scrollHeight, 300) + 'px';
        }

        if (editCardDescription) {
            editCardDescription.addEventListener('input', function() {
                updateDescriptionPreview();
                autoResizeTextarea(editCardDescription);
            });
        }

        var btnToggleDescriptionEdit = document.getElementById("btnToggleDescriptionEdit");
        var isDescriptionEditing = false;

        function setDescriptionEditMode(editing) {
            isDescriptionEditing = editing;
            if (!editCardDescriptionPreview) {
                editCardDescriptionPreview = document.getElementById("editCardDescriptionPreview");
            }
            var hasContent = (editCardDescription.value || "").trim().length > 0;

            if (editing) {
                editCardDescription.style.display = "";
                autoResizeTextarea(editCardDescription);
                if (hasContent && editCardDescriptionPreview) {
                    editCardDescriptionPreview.style.display = "";
                }
                if (btnToggleDescriptionEdit) {
                    btnToggleDescriptionEdit.innerHTML = '<i data-lucide="eye" style="width:14px;height:14px"></i> Preview';
                    lucide.createIcons({ nodes: [btnToggleDescriptionEdit] });
                }
            } else {
                editCardDescription.style.display = "none";
                if (hasContent) {
                    if (editCardDescriptionPreview) editCardDescriptionPreview.style.display = "";
                    if (btnToggleDescriptionEdit) {
                        btnToggleDescriptionEdit.classList.remove("d-none");
                        btnToggleDescriptionEdit.innerHTML = '<i data-lucide="edit-3" style="width:14px;height:14px"></i> Edit';
                        lucide.createIcons({ nodes: [btnToggleDescriptionEdit] });
                    }
                } else {
                    if (editCardDescriptionPreview) editCardDescriptionPreview.style.display = "none";
                    if (btnToggleDescriptionEdit) {
                        btnToggleDescriptionEdit.classList.add("d-none");
                    }
                }
            }
        }

        if (btnToggleDescriptionEdit) {
            btnToggleDescriptionEdit.addEventListener("click", function() {
                setDescriptionEditMode(!isDescriptionEditing);
            });
        }

        document.addEventListener("click", function(e) {
            var img = e.target.closest("img[data-fullscreen-src]");
            if (img) {
                e.stopPropagation();
                openImageFullscreen(img.getAttribute("data-fullscreen-src"));
            }
        });

        function openCardEditModal(cardEl) {
            if (!editCardModal || !editCardTitle || !editCardDescription || !editCardPlannedStart || !editCardDueDate || !editCardActualStart || !editCardActualEnd) {
                return;
            }

            showEditCardView("details");
            currentEditCardElement = cardEl;
            currentEditCardId = parseInt(cardEl.dataset.cardId, 10);
            currentEditLabels = parseCardLabels(cardEl).map(function(label) {
                return { Id: label.Id, Name: label.Name, Color: label.Color };
            });
            currentEditLabels.sort(function(a, b) { return (a.Name || "").localeCompare(b.Name || ""); });

            editCardTitle.classList.remove("is-invalid");
            editCardTitle.value = cardEl.dataset.title || "";
            editCardDescription.value = cardEl.dataset.description || "";
            updateDescriptionPreview();
            autoResizeTextarea(editCardDescription);
            if (canEditCurrentBoard) {
                var hasDesc = (cardEl.dataset.description || "").trim().length > 0;
                setDescriptionEditMode(!hasDesc);
            } else if (!(cardEl.dataset.description || "").trim()) {
                editCardDescription.style.display = "";
                editCardDescriptionPreview.style.display = "none";
            } else {
                editCardDescription.style.display = "none";
                editCardDescriptionPreview.style.display = "";
            }
            editCardPlannedStart.value = cardEl.dataset.plannedStart || "";
            editCardDueDate.value = cardEl.dataset.dueDate || "";
            if (editCardPriority) {
                editCardPriority.value = cardEl.dataset.priority || "4";
            }
            if (editCardLabelInput) {
                editCardLabelInput.value = "";
            }
            if (editCardAssignee && !canEditCurrentBoard) {
                editCardAssignee.innerHTML = '<option value="">' + getLocalizedText("unassigned", "Unassigned") + '</option>';
                if (cardEl.dataset.assignedUserId) {
                    var option = document.createElement("option");
                    option.value = cardEl.dataset.assignedUserId;
                    option.textContent = cardEl.dataset.assignedUserName || cardEl.dataset.assignedUserInitial;
                    editCardAssignee.appendChild(option);
                    editCardAssignee.value = cardEl.dataset.assignedUserId;
                }
            }
            editCardActualStart.textContent = formatDateTimeLocal(cardEl.dataset.actualStart);
            editCardActualEnd.textContent = formatDateTimeLocal(cardEl.dataset.actualEnd);

            // Populate creator info
            var creatorInitial = cardEl.dataset.creatorUserInitial || "";
            var creatorAvatarUrl = cardEl.dataset.creatorUserAvatarUrl || "";
            var creatorName = cardEl.dataset.creatorUserName || creatorInitial || "";
            var creationTime = formatDateTimeLocal(cardEl.dataset.creationTime);
            if (editCardCreatorName) editCardCreatorName.textContent = creatorName || "—";
            if (editCardCreationTime) editCardCreationTime.textContent = creationTime;
            if (editCardCreatorAvatar) {
                editCardCreatorAvatar.title = creatorName || "";
                var avatarInner = editCardCreatorAvatar.querySelector("img");
                if (creatorAvatarUrl) {
                    if (!avatarInner) {
                        avatarInner = document.createElement("img");
                        avatarInner.className = "card-assignee-avatar-image";
                        editCardCreatorAvatar.insertBefore(avatarInner, editCardCreatorInitial);
                    }
                    avatarInner.src = creatorAvatarUrl;
                    avatarInner.alt = creatorName;
                    avatarInner.onerror = function() { this.classList.add("d-none"); editCardCreatorInitial.classList.remove("d-none"); };
                    avatarInner.classList.remove("d-none");
                    editCardCreatorInitial.classList.add("d-none");
                } else {
                    if (avatarInner) avatarInner.classList.add("d-none");
                    editCardCreatorInitial.classList.remove("d-none");
                    editCardCreatorInitial.textContent = creatorInitial;
                }
            }

            renderSelectedLabels();
            loadBoardMembers(cardEl.dataset.assignedUserId || "");
            loadComments(currentEditCardId);
            new bootstrap.Modal(editCardModal).show();
        }

        if (btnUpdateCard) {
            btnUpdateCard.addEventListener("click", function() {
                var title = editCardTitle.value.trim();
                if (!title) {
                    editCardTitle.classList.add("is-invalid");
                    return;
                }

                var description = editCardDescription.value.trim();
                var plannedStart = editCardPlannedStart.value;
                var dueDate = editCardDueDate.value;
                var priority = editCardPriority ? editCardPriority.value : "4";
                var assignedUserId = editCardAssignee ? editCardAssignee.value : "";

                btnUpdateCard.disabled = true;
                btnUpdateCard.textContent = getLocalizedText("saving", "Saving...");

                fetch("/Kanban/UpdateCardDetails", {
                    method: "POST",
                    headers: { "Content-Type": "application/x-www-form-urlencoded", "RequestVerificationToken": csrfToken },
                    body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken)
                        + "&cardId=" + currentEditCardId
                        + "&title=" + encodeURIComponent(title)
                        + "&description=" + encodeURIComponent(description)
                        + "&plannedStartTime=" + encodeURIComponent(plannedStart)
                        + "&dueDate=" + encodeURIComponent(dueDate)
                        + "&priority=" + encodeURIComponent(priority)
                        + "&assignedUserId=" + encodeURIComponent(assignedUserId)
                })
                .then(function(r) {
                    if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("server-error", "Server error") + " " + r.status); });
                    return r.json();
                })
                .then(function(data) {
                    if (currentEditCardElement) {
                        currentEditCardElement.dataset.title = data.Title;
                        currentEditCardElement.dataset.description = data.Description || "";
                        currentEditCardElement.dataset.plannedStart = data.PlannedStartTime || "";
                        currentEditCardElement.dataset.dueDate = data.DueDate || "";
                        currentEditCardElement.dataset.actualStart = data.ActualStartTime || "";
                        currentEditCardElement.dataset.actualEnd = data.ActualEndTime || "";
                        currentEditCardElement.dataset.priority = (data.Priority !== undefined ? data.Priority : 4).toString();
                        updateCardAssignment(currentEditCardElement, data.AssignedUserId, data.AssignedUserName, data.AssignedUserInitial, data.AssignedUserAvatarUrl);
                        renderCardContent(currentEditCardElement);
                    }
                    bootstrap.Modal.getInstance(editCardModal).hide();
                })
                .catch(function(err) {
                    showFriendlyDialog(getLocalizedText("failed-save", "Failed to save:") + " " + (err.message || getLocalizedText("unknown-error", "Unknown error")), getLocalizedText("error", "Error"));
                })
                .finally(function() {
                    btnUpdateCard.disabled = false;
                    btnUpdateCard.textContent = getLocalizedText("save", "Save");
                });
            });
        }

        function getCurrentCardColumn() {
            return currentEditCardElement ? currentEditCardElement.closest(".kanban-column") : null;
        }

        function resetMoveTargetColumns() {
            if (!moveTargetColumn || !btnConfirmMoveCard) return;

            moveTargetColumn.innerHTML = "";
            var placeholder = document.createElement("option");
            placeholder.value = "";
            placeholder.textContent = getLocalizedText("select-column", "Select a column");
            moveTargetColumn.appendChild(placeholder);

            var currentColumn = getCurrentCardColumn();
            var currentColumnId = currentColumn ? currentColumn.dataset.columnId : "";
            getKanbanColumns().forEach(function(column) {
                if (column.dataset.columnId === currentColumnId) return;

                var option = document.createElement("option");
                option.value = column.dataset.columnId;
                option.textContent = getColumnTitle(column);
                moveTargetColumn.appendChild(option);
            });

            moveTargetColumn.value = "";
            btnConfirmMoveCard.disabled = true;
            if (moveCardError) {
                clearInlineAlert(moveCardError);
            }
        }

        function moveCurrentCardToColumn(targetColumnId, data) {
            if (!currentEditCardElement) return;

            var targetList = document.querySelector('.column-cards[data-column-id="' + targetColumnId + '"]');
            if (!targetList) return;

            targetList.appendChild(currentEditCardElement);
            currentEditCardElement.dataset.actualStart = data.ActualStartTime || "";
            currentEditCardElement.dataset.actualEnd = data.ActualEndTime || "";
            renderCardContent(currentEditCardElement);
            refreshColumnCounts();
            var targetColumn = targetList.closest(".kanban-column");
            var columns = getKanbanColumns();
            var targetIndex = columns.indexOf(targetColumn);
            if (targetIndex >= 0) {
                scrollToMobileColumn(targetIndex);
            }
        }

        if (btnMoveCard && moveTargetColumn && btnConfirmMoveCard) {
            btnMoveCard.addEventListener("click", function() {
                resetMoveTargetColumns();
                showEditCardView("move");
            });

            moveTargetColumn.addEventListener("change", function() {
                btnConfirmMoveCard.disabled = !moveTargetColumn.value;
            });
        }

        if (btnBackFromMoveCard) {
            btnBackFromMoveCard.addEventListener("click", function() {
                showEditCardView("details");
            });
        }

        if (btnConfirmMoveCard && moveTargetColumn) {
            btnConfirmMoveCard.addEventListener("click", function() {
                if (!currentEditCardId || !moveTargetColumn.value) return;

                var targetColumnId = moveTargetColumn.value;
                var targetList = document.querySelector('.column-cards[data-column-id="' + targetColumnId + '"]');
                var newOrder = targetList ? targetList.querySelectorAll(".kanban-card").length : 0;

                btnConfirmMoveCard.disabled = true;
                btnConfirmMoveCard.textContent = getLocalizedText("moving", "Moving...");
                if (moveCardError) {
                    clearInlineAlert(moveCardError);
                }

                fetch("/Kanban/MoveCard", {
                    method: "POST",
                    headers: { "Content-Type": "application/x-www-form-urlencoded", "RequestVerificationToken": csrfToken },
                    body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken)
                        + "&cardId=" + currentEditCardId
                        + "&targetColumnId=" + encodeURIComponent(targetColumnId)
                        + "&newOrder=" + newOrder
                })
                .then(function(r) {
                    if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("server-error", "Server error") + " " + r.status); });
                    return r.json();
                })
                .then(function(data) {
                    moveCurrentCardToColumn(targetColumnId, data);
                    var editCardModalInstance = bootstrap.Modal.getInstance(editCardModal);
                    if (editCardModalInstance) {
                        editCardModalInstance.hide();
                    }
                })
                .catch(function(err) {
                    if (moveCardError) {
                        showInlineAlert(moveCardError, getLocalizedText("failed-move", "Failed to move card:") + " " + (err.message || getLocalizedText("unknown-error", "Unknown error")));
                    }
                })
                .finally(function() {
                    btnConfirmMoveCard.disabled = !moveTargetColumn.value;
                    btnConfirmMoveCard.textContent = getLocalizedText("move", "Move");
                });
            });
        }

        if (btnDeleteCard) {
            btnDeleteCard.addEventListener("click", function() {
                if (deleteCardError) {
                    clearInlineAlert(deleteCardError);
                }
                if (btnConfirmDeleteCard) {
                    btnConfirmDeleteCard.disabled = false;
                    btnConfirmDeleteCard.textContent = getLocalizedText("delete", "Delete");
                }

                showEditCardView("delete");
            });
        }

        if (btnBackFromDeleteCard) {
            btnBackFromDeleteCard.addEventListener("click", function() {
                showEditCardView("details");
            });
        }

        if (btnConfirmDeleteCard) {
            btnConfirmDeleteCard.addEventListener("click", function() {
                if (!currentEditCardId) return;

                btnConfirmDeleteCard.disabled = true;
                btnConfirmDeleteCard.textContent = getLocalizedText("deleting", "Deleting...");
                if (deleteCardError) {
                    clearInlineAlert(deleteCardError);
                }

                fetch("/Kanban/DeleteCard", {
                    method: "POST",
                    headers: { "Content-Type": "application/x-www-form-urlencoded", "RequestVerificationToken": csrfToken },
                    body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken) + "&cardId=" + currentEditCardId
                })
                .then(function(r) {
                    if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("server-error", "Server error") + " " + r.status); });
                    if (currentEditCardElement) {
                        currentEditCardElement.remove();
                        refreshColumnCounts();
                    }
                    currentEditCardId = 0;
                    currentEditCardElement = null;
                    var editCardModalInstance = bootstrap.Modal.getInstance(editCardModal);
                    if (editCardModalInstance) {
                        editCardModalInstance.hide();
                    }
                })
                .catch(function(err) {
                    if (deleteCardError) {
                        showInlineAlert(deleteCardError, err.message || getLocalizedText("server-error", "Server error"));
                    }
                })
                .finally(function() {
                    btnConfirmDeleteCard.disabled = false;
                    btnConfirmDeleteCard.textContent = getLocalizedText("delete", "Delete");
                });
            });
        }

        function resetTransferSelect(select, placeholder) {
            if (!select) return;
            select.innerHTML = "";
            var option = document.createElement("option");
            option.value = "";
            option.textContent = placeholder;
            select.appendChild(option);
        }

        function showTransferError(message) {
            if (!transferCardError) return;
            if (message) {
                showInlineAlert(transferCardError, message);
            } else {
                clearInlineAlert(transferCardError);
            }
        }

        function updateTransferColumns() {
            if (!transferTargetBoard || !transferTargetColumn || !btnConfirmTransferCard) return;

            resetTransferSelect(transferTargetColumn, getLocalizedText("select-column", "Select a column"));
            var boardId = parseInt(transferTargetBoard.value, 10);
            var board = transferTargets.find(function(target) { return target.Id === boardId; });
            if (!board) {
                transferTargetColumn.disabled = true;
                btnConfirmTransferCard.disabled = true;
                return;
            }

            board.Columns.forEach(function(column) {
                var option = document.createElement("option");
                option.value = column.Id;
                option.textContent = column.Name;
                transferTargetColumn.appendChild(option);
            });
            transferTargetColumn.disabled = false;
            btnConfirmTransferCard.disabled = true;
        }

        if (transferTargetBoard) {
            transferTargetBoard.addEventListener("change", updateTransferColumns);
        }

        if (transferTargetColumn && btnConfirmTransferCard) {
            transferTargetColumn.addEventListener("change", function() {
                btnConfirmTransferCard.disabled = !transferTargetBoard.value || !transferTargetColumn.value;
            });
        }

        if (btnBackFromTransferCard) {
            btnBackFromTransferCard.addEventListener("click", function() {
                showEditCardView("details");
            });
        }

        if (btnTransferCard && transferTargetBoard && transferTargetColumn && btnConfirmTransferCard) {
            btnTransferCard.addEventListener("click", function() {
                transferTargets = [];
                resetTransferSelect(transferTargetBoard, getLocalizedText("loading-transfer-targets", "Loading target boards..."));
                resetTransferSelect(transferTargetColumn, getLocalizedText("select-column", "Select a column"));
                transferTargetBoard.disabled = true;
                transferTargetColumn.disabled = true;
                btnConfirmTransferCard.disabled = true;
                showTransferError("");
                showEditCardView("transfer");

                fetch("/Kanban/GetTransferTargets?cardId=" + currentEditCardId)
                    .then(function(r) {
                        if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("failed-load-transfer-targets", "Failed to load transfer targets.")); });
                        return r.json();
                    })
                    .then(function(targets) {
                        transferTargets = targets;
                        resetTransferSelect(transferTargetBoard, getLocalizedText("select-board", "Select a board"));
                        targets.forEach(function(board) {
                            var option = document.createElement("option");
                            option.value = board.Id;
                            option.textContent = board.Name;
                            transferTargetBoard.appendChild(option);
                        });
                        transferTargetBoard.disabled = targets.length === 0;
                        if (targets.length === 0) {
                            showTransferError(getLocalizedText("no-transfer-targets", "No editable target boards are available."));
                        }
                    })
                    .catch(function(err) {
                        resetTransferSelect(transferTargetBoard, getLocalizedText("select-board", "Select a board"));
                        showTransferError(err.message || getLocalizedText("failed-load-transfer-targets", "Failed to load transfer targets."));
                    });
            });
        }

        if (btnConfirmTransferCard && transferTargetBoard && transferTargetColumn) {
            btnConfirmTransferCard.addEventListener("click", function() {
                if (!transferTargetBoard.value || !transferTargetColumn.value) return;

                btnConfirmTransferCard.disabled = true;
                btnConfirmTransferCard.textContent = getLocalizedText("transferring", "Transferring...");
                showTransferError("");

                fetch("/Kanban/TransferCard", {
                    method: "POST",
                    headers: { "Content-Type": "application/x-www-form-urlencoded", "RequestVerificationToken": csrfToken },
                    body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken)
                        + "&cardId=" + currentEditCardId
                        + "&targetBoardId=" + encodeURIComponent(transferTargetBoard.value)
                        + "&targetColumnId=" + encodeURIComponent(transferTargetColumn.value)
                })
                .then(function(r) {
                    if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("server-error", "Server error") + " " + r.status); });
                    if (currentEditCardElement) {
                        currentEditCardElement.remove();
                        refreshColumnCounts();
                    }
                    currentEditCardId = 0;
                    currentEditCardElement = null;
                    var editCardModalInstance = bootstrap.Modal.getInstance(editCardModal);
                    if (editCardModalInstance) {
                        editCardModalInstance.hide();
                    }
                })
                .catch(function(err) {
                    showTransferError(getLocalizedText("failed-transfer", "Failed to transfer card:") + " " + (err.message || getLocalizedText("unknown-error", "Unknown error")));
                })
                .finally(function() {
                    btnConfirmTransferCard.disabled = !transferTargetBoard.value || !transferTargetColumn.value;
                    btnConfirmTransferCard.textContent = getLocalizedText("transfer", "Transfer");
                });
            });
        }

        if (canEditCurrentBoard) {
            var btnEditBoardTitle = document.querySelector(".btn-edit-board-title");
            if (btnEditBoardTitle) {
                btnEditBoardTitle.addEventListener("click", function(e) {
                    e.stopPropagation();
                    var titleSpan = document.querySelector(".board-title-display");
                    if (!titleSpan || titleSpan.querySelector("input")) return;

                    var currentName = titleSpan.textContent;
                    var input = document.createElement("input");
                    input.type = "text";
                    input.className = "board-title-input";
                    input.value = currentName;
                    input.dataset.originalName = currentName;

                    titleSpan.style.display = "none";
                    titleSpan.parentNode.insertBefore(input, titleSpan.nextSibling);

                    btnEditBoardTitle.style.display = "none";

                    input.focus();
                    input.select();

                    function saveBoardRename() {
                        var newName = input.value.trim();
                        if (!newName || newName === input.dataset.originalName) {
                            cancelBoardRename();
                            return;
                        }

                        fetch("/Kanban/RenameBoard", {
                            method: "POST",
                            headers: {
                                "Content-Type": "application/x-www-form-urlencoded",
                                "RequestVerificationToken": csrfToken
                            },
                            body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken)
                                + "&boardId=" + currentBoardId
                                + "&name=" + encodeURIComponent(newName)
                        })
                        .then(function(r) {
                            if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("server-error", "Server error") + " " + r.status); });
                            return r.json();
                        })
                        .then(function() {
                            titleSpan.textContent = newName;
                            titleSpan.style.display = "";
                            btnEditBoardTitle.style.display = "";
                            input.remove();
                        })
                        .catch(function(err) {
                            showFriendlyDialog(getLocalizedText("failed-rename-board", "Failed to rename board:") + " " + (err.message || getLocalizedText("unknown-error", "Unknown error")), getLocalizedText("error", "Error"));
                            cancelBoardRename();
                        });
                    }

                    function cancelBoardRename() {
                        titleSpan.style.display = "";
                        btnEditBoardTitle.style.display = "";
                        input.remove();
                    }

                    input.addEventListener("keydown", function(ev) {
                        if (ev.key === "Enter") {
                            ev.preventDefault();
                            saveBoardRename();
                        } else if (ev.key === "Escape") {
                            ev.preventDefault();
                            cancelBoardRename();
                        }
                    });

                    input.addEventListener("blur", function() {
                        saveBoardRename();
                    });
                });
            }
        }

        if (btnAddComment && commentInput) {
            function submitComment() {
                var content = commentInput.value.trim();
                if (!content) return;
                if (content.length > 2000) {
                    showFriendlyDialog(getLocalizedText("comment-too-long", "Comment is too long (max 2000 characters)."), getLocalizedText("warning", "Warning"), "alert-triangle");
                    return;
                }
                btnAddComment.disabled = true;
                btnAddComment.textContent = getLocalizedText("sending", "Sending...");
                fetch("/Kanban/AddComment", {
                    method: "POST",
                    headers: { "Content-Type": "application/x-www-form-urlencoded", "RequestVerificationToken": csrfToken },
                    body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken)
                        + "&cardId=" + currentEditCardId
                        + "&content=" + encodeURIComponent(content)
                })
                .then(function(r) {
                    if (!r.ok) return r.text().then(function(t) { throw new Error(t || getLocalizedText("failed-add-comment", "Failed to add comment.")); });
                    return r.json();
                })
                .then(function() {
                    commentInput.value = "";
                    loadComments(currentEditCardId);
                })
                .catch(function(err) {
                    showFriendlyDialog(err.message || getLocalizedText("failed-add-comment", "Failed to add comment."), getLocalizedText("error", "Error"));
                })
                .finally(function() {
                    btnAddComment.disabled = false;
                    btnAddComment.textContent = getLocalizedText("send", "Send");
                });
            }

            btnAddComment.addEventListener("click", submitComment);

            commentInput.addEventListener("keydown", function(e) {
                if ((e.ctrlKey || e.metaKey) && e.key === "Enter") {
                    e.preventDefault();
                    submitComment();
                }
            });
        }

        function deleteComment(commentId) {
            commentIdToDelete = commentId;
            if (deleteCommentError) {
                clearInlineAlert(deleteCommentError);
            }
            if (btnConfirmDeleteComment) {
                btnConfirmDeleteComment.disabled = false;
                btnConfirmDeleteComment.textContent = getLocalizedText("delete-comment", "Delete");
            }
            if (deleteCommentConfirmModal) {
                deleteCommentConfirmModal.show();
            }
        }

        if (btnConfirmDeleteComment) {
            btnConfirmDeleteComment.addEventListener("click", function() {
                if (!commentIdToDelete) return;

                btnConfirmDeleteComment.disabled = true;
                btnConfirmDeleteComment.textContent = getLocalizedText("deleting", "Deleting...");
                if (deleteCommentError) {
                    clearInlineAlert(deleteCommentError);
                }

                fetch("/Kanban/DeleteComment?commentId=" + commentIdToDelete, {
                    method: "POST",
                    headers: { "RequestVerificationToken": csrfToken }
                })
                .then(function(r) {
                    if (!r.ok) throw new Error(getLocalizedText("failed-delete-comment", "Failed to delete comment."));
                    if (deleteCommentConfirmModal) {
                        deleteCommentConfirmModal.hide();
                    }
                    loadComments(currentEditCardId);
                })
                .catch(function(err) {
                    if (deleteCommentError) {
                        showInlineAlert(deleteCommentError, err.message || getLocalizedText("failed-delete-comment", "Failed to delete comment."));
                    }
                })
                .finally(function() {
                    if (btnConfirmDeleteComment) {
                        btnConfirmDeleteComment.disabled = false;
                        btnConfirmDeleteComment.textContent = getLocalizedText("delete-comment", "Delete");
                    }
                });
            });
        }
    });
