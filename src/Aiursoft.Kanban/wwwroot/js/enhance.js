/**
 * setupImageDropzone
 * Wraps an HTML element (like a textarea) to support drag-and-drop and pasting of images.
 * Displays image previews above the element, and adds user hints for dragging/pasting.
 * 
 * @param {HTMLElement} element - The HTML element to wrap
 * @param {Object} options - Configuration options
 * @param {Function} options.onImagesChange - Callback when images are added/removed. Passed an array of File objects.
 * @returns {Object} - An object containing methods to interact with the dropzone
 */
function setupImageDropzone(element, options = {}) {
    if (!element) return null;

    // 1. Create the wrapper
    const wrapper = document.createElement('div');
    wrapper.className = 'image-dropzone-wrapper';
    
    // Set styles for the wrapper so it behaves like the original element in flex layouts
    wrapper.style.position = 'relative';
    wrapper.style.display = 'flex';
    wrapper.style.flexDirection = 'column';
    wrapper.style.flex = '1';
    wrapper.style.minWidth = '0';

    // Insert wrapper before element, then move element into wrapper
    element.parentNode.insertBefore(wrapper, element);

    // 2. Create the preview container (above the element)
    const previewContainer = document.createElement('div');
    previewContainer.className = 'image-dropzone-preview';
    previewContainer.style.display = 'none'; // Hidden initially
    previewContainer.style.flexWrap = 'wrap';
    previewContainer.style.gap = '0.5rem';
    previewContainer.style.marginBottom = '0.5rem';

    // 3. Create the bottom hint text
    const hintText = document.createElement('div');
    hintText.className = 'image-dropzone-hint';
    hintText.style.fontSize = '0.75rem';
    hintText.style.color = 'var(--bs-secondary-color, #868e96)';
    hintText.style.marginTop = '0.35rem';
    hintText.style.display = 'flex';
    hintText.style.alignItems = 'center';
    hintText.style.gap = '0.3rem';
    hintText.style.userSelect = 'none';
    hintText.innerHTML = `
        <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
            <polyline points="17 8 12 3 7 8"></polyline>
            <line x1="12" y1="3" x2="12" y2="15"></line>
        </svg>
        <span>您可以拖拽图片，或使用剪贴板粘贴到此处</span>
    `;

    // 4. Create an overlay for dragover feedback
    const overlay = document.createElement('div');
    overlay.className = 'image-dropzone-overlay';
    overlay.style.position = 'absolute';
    overlay.style.top = '0';
    overlay.style.left = '0';
    overlay.style.right = '0';
    overlay.style.bottom = '0';
    // Use the primary color from CSS variables with some opacity
    overlay.style.backgroundColor = 'rgba(255, 255, 255, 0.85)';
    overlay.style.border = '2px dashed var(--bs-primary, #4dabf7)';
    overlay.style.borderRadius = '8px';
    overlay.style.display = 'none'; // Hidden by default
    overlay.style.alignItems = 'center';
    overlay.style.justifyContent = 'center';
    overlay.style.zIndex = '10';
    overlay.style.pointerEvents = 'none'; // Let drop events pass through to the wrapper
    overlay.innerHTML = `
        <div style="color: var(--bs-primary, #4dabf7); font-weight: 600; display: flex; align-items: center; gap: 0.5rem; font-size: 0.9rem;">
            <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
                <circle cx="8.5" cy="8.5" r="1.5"></circle>
                <polyline points="21 15 16 10 5 21"></polyline>
            </svg>
            松开鼠标添加图片
        </div>
    `;

    // Append everything in correct order
    wrapper.appendChild(previewContainer);
    wrapper.appendChild(element); // Move original element inside
    wrapper.appendChild(hintText);
    wrapper.appendChild(overlay);

    // 5. State management for files
    let filesList = [];

    function triggerChange() {
        if (typeof options.onImagesChange === 'function') {
            options.onImagesChange(filesList);
        }
    }

    // 6. Render image previews
    function renderPreviews() {
        previewContainer.innerHTML = '';
        if (filesList.length === 0) {
            previewContainer.style.display = 'none';
        } else {
            previewContainer.style.display = 'flex';
        }

        filesList.forEach((file, index) => {
            const imgWrapper = document.createElement('div');
            imgWrapper.style.position = 'relative';
            imgWrapper.style.display = 'inline-block';

            const img = document.createElement('img');
            img.src = URL.createObjectURL(file);
            img.style.height = '60px'; // Small thumbnail height
            img.style.maxWidth = '100px';
            img.style.objectFit = 'cover';
            img.style.borderRadius = '6px';
            img.style.border = '1px solid var(--bs-border-color, #dee2e6)';
            img.style.boxShadow = '0 1px 3px rgba(0,0,0,0.1)';

            const removeBtn = document.createElement('button');
            removeBtn.innerHTML = '&times;';
            removeBtn.type = 'button';
            removeBtn.title = 'Remove image';
            
            // Style the remove button
            removeBtn.style.position = 'absolute';
            removeBtn.style.top = '-6px';
            removeBtn.style.right = '-6px';
            removeBtn.style.background = 'var(--bs-danger, #dc3545)';
            removeBtn.style.color = 'white';
            removeBtn.style.border = 'none';
            removeBtn.style.borderRadius = '50%';
            removeBtn.style.width = '20px';
            removeBtn.style.height = '20px';
            removeBtn.style.cursor = 'pointer';
            removeBtn.style.display = 'flex';
            removeBtn.style.alignItems = 'center';
            removeBtn.style.justifyContent = 'center';
            removeBtn.style.fontSize = '14px';
            removeBtn.style.lineHeight = '1';
            removeBtn.style.padding = '0';
            removeBtn.style.boxShadow = '0 1px 3px rgba(0,0,0,0.2)';
            removeBtn.style.transition = 'transform 0.1s ease';

            removeBtn.onmouseenter = () => removeBtn.style.transform = 'scale(1.1)';
            removeBtn.onmouseleave = () => removeBtn.style.transform = 'scale(1)';

            removeBtn.onclick = (e) => {
                e.preventDefault();
                filesList.splice(index, 1);
                renderPreviews();
                triggerChange();
            };

            imgWrapper.appendChild(img);
            imgWrapper.appendChild(removeBtn);
            previewContainer.appendChild(imgWrapper);
        });
    }

    // 7. Helper to add and filter image files
    function addFiles(newFiles) {
        let added = false;
        for (let i = 0; i < newFiles.length; i++) {
            const file = newFiles[i];
            if (file.type.startsWith('image/')) {
                filesList.push(file);
                added = true;
            }
        }
        if (added) {
            renderPreviews();
            triggerChange();
        }
    }

    // 8. Drag and Drop events on the wrapper
    let dragCounter = 0;

    wrapper.addEventListener('dragenter', (e) => {
        e.preventDefault();
        dragCounter++;
        overlay.style.display = 'flex'; // Show drop hint overlay
    });

    wrapper.addEventListener('dragover', (e) => {
        e.preventDefault(); // Necessary to allow dropping
    });

    wrapper.addEventListener('dragleave', (e) => {
        e.preventDefault();
        dragCounter--;
        if (dragCounter === 0) {
            overlay.style.display = 'none'; // Hide overlay
        }
    });

    wrapper.addEventListener('drop', (e) => {
        e.preventDefault();
        dragCounter = 0;
        overlay.style.display = 'none'; // Hide overlay
        
        if (e.dataTransfer && e.dataTransfer.files) {
            addFiles(e.dataTransfer.files);
        }
    });

    // 9. Paste events on the element
    element.addEventListener('paste', (e) => {
        if (e.clipboardData && e.clipboardData.items) {
            const items = e.clipboardData.items;
            const files = [];
            for (let i = 0; i < items.length; i++) {
                if (items[i].type.indexOf('image') !== -1) {
                    const file = items[i].getAsFile();
                    if (file) files.push(file);
                }
            }
            if (files.length > 0) {
                // Prevent default text paste if you only want the image
                addFiles(files);
            }
        }
    });

    // Return API
    return {
        getFiles: () => filesList,
        clearFiles: () => {
            filesList = [];
            renderPreviews();
            triggerChange();
        }
    };
}

/**
 * 突出卡片，在需要卡片引人注意的时候使用
 * @param {HTMLElement} cardEle 卡片元素
 * @param {number} duration 持续时间
 */
var _hightLightStyleInjected = false;
function hightLightCard(cardEle) {
    if (!cardEle) return;

    if (!_hightLightStyleInjected) {
        var style = document.createElement("style");
        style.textContent =
            "@keyframes kanban-card-pulse {" +
            "  0%, 100% { background: var(--bs-body-bg, #fff); border-color: var(--bs-border-color, #e9ecef); box-shadow: 0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04); transform: scale(1); }" +
            "  25%  { background: #fff9db; border-color: #fcc419; box-shadow: 0 4px 16px rgba(252,196,25,0.45), 0 0 0 4px rgba(252,196,25,0.3), 0 8px 24px rgba(0,0,0,0.12); transform: scale(1.02); }" +
            "  50%  { background: #fff3bf; border-color: #fcc419; box-shadow: 0 8px 28px rgba(252,196,25,0.55), 0 0 0 6px rgba(252,196,25,0.25), 0 16px 40px rgba(0,0,0,0.16); transform: scale(1.03); }" +
            "  75%  { background: #fff9db; border-color: #fcc419; box-shadow: 0 4px 16px rgba(252,196,25,0.45), 0 0 0 4px rgba(252,196,25,0.3), 0 8px 24px rgba(0,0,0,0.12); transform: scale(1.02); }" +
            "}" +
            ".kanban-card.hightlight-pulse {" +
            "  animation: kanban-card-pulse 0.8s ease-in-out 3;" +
            "}";
        document.head.appendChild(style);
        _hightLightStyleInjected = true;
    }

    cardEle.classList.add("hightlight-pulse");

    cardEle.addEventListener("animationend", function handler() {
        cardEle.classList.remove("hightlight-pulse");
        cardEle.removeEventListener("animationend", handler);
    }, { once: true });
}