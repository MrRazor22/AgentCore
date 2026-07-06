let currentSessionId = null;
let activeController = null;

// DOM Elements
const sessionsList = document.getElementById('sessions-list');
const newChatBtn = document.getElementById('new-chat-btn');
const chatFeed = document.getElementById('chat-feed');
const welcomeMessage = document.getElementById('welcome-message');
const chatInput = document.getElementById('chat-input');
const sendBtn = document.getElementById('send-btn');
const stopBtn = document.getElementById('stop-btn');
const logsConsole = document.getElementById('logs-console');

// Telemetry Elements
const statModel = document.getElementById('stat-model');
const statTokens = document.getElementById('stat-tokens');
const statElapsed = document.getElementById('stat-elapsed');
const statMemory = document.getElementById('stat-memory');

// Code Modal Elements
const viewCodeBtn = document.getElementById('view-code-btn');
const codeModal = document.getElementById('code-modal');

// Init
window.addEventListener('DOMContentLoaded', () => {
    loadSessions();
    newChat();

    // Modal Triggers
    viewCodeBtn.addEventListener('click', () => codeModal.style.display = 'flex');
    codeModal.addEventListener('click', (e) => {
        if (e.target === codeModal) closeModal();
    });

    // Send handlers
    sendBtn.addEventListener('click', sendMessage);
    chatInput.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            sendMessage();
        }
    });

    newChatBtn.addEventListener('click', newChat);
    stopBtn.addEventListener('click', stopGeneration);
});

function closeModal() {
    codeModal.style.display = 'none';
}

function setPrompt(text) {
    chatInput.value = text;
    chatInput.focus();
}

// Generate UUID for sessions
function generateUUID() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
        var r = Math.random() * 16 | 0, v = c == 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
    });
}

// Load Sessions list
async function loadSessions() {
    try {
        const res = await fetch('/api/sessions');
        const sessions = await res.json();
        
        sessionsList.innerHTML = '';
        
        sessions.forEach(session => {
            const item = document.createElement('div');
            item.className = `session-item ${session.id === currentSessionId ? 'active' : ''}`;
            item.setAttribute('data-id', session.id);
            item.addEventListener('click', () => selectSession(session.id));

            const details = document.createElement('div');
            details.className = 'session-details';

            const title = document.createElement('span');
            title.className = 'session-title';
            title.textContent = session.title;

            const meta = document.createElement('span');
            meta.className = 'session-meta';
            meta.textContent = new Date(session.lastUpdated).toLocaleTimeString();

            details.appendChild(title);
            details.appendChild(meta);

            const delBtn = document.createElement('button');
            delBtn.className = 'session-delete';
            delBtn.innerHTML = '&times;';
            delBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                deleteSession(session.id);
            });

            item.appendChild(details);
            item.appendChild(delBtn);
            
            sessionsList.appendChild(item);
        });
    } catch (err) {
        logToConsole('Error loading sessions: ' + err.message, 'error');
    }
}

// Select a session and populate its history
async function selectSession(id) {
    if (activeController) stopGeneration();
    currentSessionId = id;
    
    // Highlight active session
    document.querySelectorAll('.session-item').forEach(item => {
        item.classList.toggle('active', item.getAttribute('data-id') === id);
    });

    welcomeMessage.style.display = 'none';
    chatFeed.innerHTML = '';
    resetPipeline();
    clearTelemetry();

    try {
        const res = await fetch(`/api/sessions/${id}`);
        const messages = await res.json();
        
        if (!messages || messages.length === 0) {
            welcomeMessage.style.display = 'flex';
            return;
        }

        // Render past messages
        messages.forEach(msg => {
            if (msg.role === 0 || msg.role === 'user') {
                renderUserMessage(msg.contents[0].value);
            } else if (msg.role === 1 || msg.role === 'assistant') {
                // An assistant turn can have reasoning, text, and tool calls
                const bubble = createAssistantBubble();
                msg.contents.forEach(content => {
                    if (content.type === 'reasoning') {
                        renderReasoning(bubble, content.thought);
                    } else if (content.type === 'text') {
                        renderText(bubble, content.value);
                    } else if (content.type === 'toolCall') {
                        renderToolCall(bubble, content.id, content.name, JSON.stringify(content.arguments));
                    }
                });
            } else if (msg.role === 2 || msg.role === 'tool') {
                // Render tool results inside the last rendered tool card matching this call_id
                const toolResult = msg.result;
                const callId = msg.call_id;
                const resultText = toolResult ? toolResult.value : 'No result';
                updateToolResultInHistory(callId, resultText);
            }
        });
        
        chatFeed.scrollTop = chatFeed.scrollHeight;
        logToConsole(`Loaded session ${id} with ${messages.length} messages.`, 'success');
        statMemory.textContent = `${messages.length} objects`;
    } catch (err) {
        logToConsole('Error loading session messages: ' + err.message, 'error');
    }
}

// Delete session
async function deleteSession(id) {
    if (confirm('Delete this session?')) {
        await fetch(`/api/sessions/${id}`, { method: 'DELETE' });
        logToConsole('Session deleted: ' + id, 'system');
        if (currentSessionId === id) {
            newChat();
        } else {
            loadSessions();
        }
    }
}

// Create new session
function newChat() {
    if (activeController) stopGeneration();
    currentSessionId = generateUUID();
    
    welcomeMessage.style.display = 'flex';
    chatFeed.innerHTML = '';
    chatInput.value = '';
    
    resetPipeline();
    clearTelemetry();
    loadSessions();
    logToConsole('Initialized new session: ' + currentSessionId, 'system');
}

// Handle sending message
async function sendMessage() {
    const text = chatInput.value.trim();
    if (!text || activeController) return;

    chatInput.value = '';
    welcomeMessage.style.display = 'none';

    // 1. Render User Message
    renderUserMessage(text);
    chatFeed.scrollTop = chatFeed.scrollHeight;

    // 2. Setup controller for stop generation
    activeController = new AbortController();
    sendBtn.style.display = 'none';
    stopBtn.style.display = 'inline-block';

    // 3. Initiate SSE connection
    try {
        const response = await fetch(`/api/sessions/${currentSessionId}/message`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ message: text }),
            signal: activeController.signal
        });

        if (!response.body) {
            throw new Error('No readable stream available in response.');
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder('utf-8');
        let buffer = '';

        let activeBubble = null;

        while (true) {
            const { value, done } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });
            const lines = buffer.split('\n');
            
            // Retain last partial line in buffer
            buffer = lines.pop();

            for (const line of lines) {
                if (line.startsWith('data: ')) {
                    const jsonStr = line.substring(6).trim();
                    if (!jsonStr) continue;

                    try {
                        const event = JSON.parse(jsonStr);
                        activeBubble = handleAgentEvent(event, activeBubble);
                    } catch (e) {
                        console.error('Failed to parse SSE event:', jsonStr, e);
                    }
                }
            }
        }
    } catch (err) {
        if (err.name === 'AbortError') {
            logToConsole('Generation aborted.', 'warn');
        } else {
            logToConsole('Streaming error: ' + err.message, 'error');
            renderSystemError(err.message);
        }
    } finally {
        generationFinished();
    }
}

// Stop generation
function stopGeneration() {
    if (activeController) {
        activeController.abort();
        activeController = null;
    }
    generationFinished();
}

function generationFinished() {
    activeController = null;
    sendBtn.style.display = 'inline-block';
    stopBtn.style.display = 'none';
    resetPipeline();
    loadSessions();
}

// Event dispatcher for UI updates and observability logs
function handleAgentEvent(event, activeBubble) {
    // 1. Process Observability/Telemetry Events (regardless of active chat bubble)
    if (event.type === 'PipelineStage') {
        const { stageName, status } = event;
        const stageEl = document.getElementById(`stage-${stageName}`);
        if (stageEl) {
            if (status === 'Active') {
                stageEl.classList.add('active');
                stageEl.classList.remove('completed');
            } else {
                stageEl.classList.remove('active');
                stageEl.classList.add('completed');
            }
        }
    } 
    else if (event.type === 'PromptBuilt') {
        logToConsole(`Prompt Context Built (${event.promptText.length} chars)`, 'info');
    }
    else if (event.type === 'LLMRequestStarted') {
        logToConsole(`Calling LLM model: ${event.model}`, 'info');
        statModel.textContent = event.model;
    }
    else if (event.type === 'LLMResponseReceived') {
        logToConsole(`LLM Response Stream Ended. In: ${event.inputTokens} tkn, Out: ${event.outputTokens} tkn. Duration: ${event.elapsedMs.toFixed(0)}ms`, 'success');
        statTokens.textContent = `${event.inputTokens} / ${event.outputTokens}`;
        statElapsed.textContent = `${(event.elapsedMs / 1000).toFixed(2)}s`;
    }
    else if (event.type === 'ToolInvoking') {
        logToConsole(`Invoking tool '${event.toolName}' with arguments: ${event.arguments}`, 'warn');
    }
    else if (event.type === 'ToolCompleted') {
        logToConsole(`Tool '${event.toolName}' returned result: ${event.result}`, 'success');
    }
    else if (event.type === 'MemoryUpdated') {
        logToConsole(`Memory pipeline updated. Current history size: ${event.historyCount} items.`, 'info');
        statMemory.textContent = `${event.historyCount} items`;
    }

    // 2. Process UI Chat Feed Events
    if (event.type === 'MessageStarted') {
        activeBubble = createAssistantBubble();
    }
    else if (event.type === 'ReasoningDelta') {
        if (!activeBubble) activeBubble = createAssistantBubble();
        renderReasoning(activeBubble, event.delta);
    }
    else if (event.type === 'TextDelta') {
        if (!activeBubble) activeBubble = createAssistantBubble();
        renderText(activeBubble, event.delta);
    }
    else if (event.type === 'ToolCallStarted') {
        if (!activeBubble) activeBubble = createAssistantBubble();
        renderToolCall(activeBubble, event.callId, event.toolName, event.arguments);
    }
    else if (event.type === 'ApprovalRequested') {
        // Find existing tool card and display Approve/Deny buttons
        const toolCard = document.getElementById(`tool-${event.callId}`);
        if (toolCard && !toolCard.querySelector('.approval-actions')) {
            const actions = document.createElement('div');
            actions.className = 'approval-actions';

            const approveBtn = document.createElement('button');
            approveBtn.className = 'btn-approve';
            approveBtn.textContent = 'Approve';
            approveBtn.onclick = () => submitApproval(event.callId, true);

            const denyBtn = document.createElement('button');
            denyBtn.className = 'btn-deny';
            denyBtn.textContent = 'Deny';
            denyBtn.onclick = () => submitApproval(event.callId, false);

            actions.appendChild(approveBtn);
            actions.appendChild(denyBtn);
            toolCard.appendChild(actions);
        }
    }
    else if (event.type === 'ToolResult') {
        updateToolResultInHistory(event.callId, event.result);
    }
    else if (event.type === 'Completed') {
        logToConsole('Agent response complete.', 'success');
    }
    else if (event.type === 'Error') {
        logToConsole('Agent error: ' + event.error, 'error');
        renderSystemError(event.error);
    }

    chatFeed.scrollTop = chatFeed.scrollHeight;
    return activeBubble;
}

// Submit Tool Approval
async function submitApproval(callId, approved) {
    const toolCard = document.getElementById(`tool-${callId}`);
    if (toolCard) {
        const actions = toolCard.querySelector('.approval-actions');
        if (actions) actions.remove();
        
        const status = document.createElement('div');
        status.style.fontWeight = 'bold';
        status.style.marginTop = '8px';
        status.style.color = approved ? 'var(--success)' : 'var(--danger)';
        status.textContent = approved ? '✓ Approved' : '✗ Denied';
        toolCard.appendChild(status);
    }

    try {
        await fetch(`/api/approvals/${callId}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ approved })
        });
        logToConsole(`User submitted approval for call ${callId}: ${approved}`, 'info');
    } catch (err) {
        logToConsole('Error sending approval: ' + err.message, 'error');
    }
}

// Helpers to render bubbles
function renderUserMessage(text) {
    const bubble = document.createElement('div');
    bubble.className = 'chat-bubble user';
    bubble.textContent = text;
    chatFeed.appendChild(bubble);
}

function createAssistantBubble() {
    const bubble = document.createElement('div');
    bubble.className = 'chat-bubble assistant';
    chatFeed.appendChild(bubble);
    return bubble;
}

function renderReasoning(bubble, delta) {
    let thoughtBlock = bubble.querySelector('.thought-block');
    if (!thoughtBlock) {
        thoughtBlock = document.createElement('div');
        thoughtBlock.className = 'thought-block';
        bubble.prepend(thoughtBlock);
    }
    thoughtBlock.textContent += delta;
}

function renderText(bubble, delta) {
    let textContainer = bubble.querySelector('.text-container');
    if (!textContainer) {
        textContainer = document.createElement('div');
        textContainer.className = 'text-container';
        bubble.appendChild(textContainer);
    }
    textContainer.textContent += delta;
}

function renderToolCall(bubble, callId, name, argsJson) {
    let toolCard = document.getElementById(`tool-${callId}`);
    if (!toolCard) {
        toolCard = document.createElement('div');
        toolCard.className = 'tool-card';
        toolCard.id = `tool-${callId}`;

        const header = document.createElement('div');
        header.className = 'tool-header';
        header.innerHTML = `<span>🔧 Tool Call: ${name}</span>`;

        const args = document.createElement('div');
        args.className = 'tool-args';
        args.textContent = argsJson;

        toolCard.appendChild(header);
        toolCard.appendChild(args);
        bubble.appendChild(toolCard);
    }
}

function updateToolResultInHistory(callId, result) {
    const toolCard = document.getElementById(`tool-${callId}`);
    if (toolCard) {
        // Remove actions if any were left over
        const actions = toolCard.querySelector('.approval-actions');
        if (actions) actions.remove();

        let resultBox = toolCard.querySelector('.tool-result-box');
        if (!resultBox) {
            resultBox = document.createElement('div');
            resultBox.className = 'tool-result-box';
            toolCard.appendChild(resultBox);
        }
        resultBox.textContent = result;
    }
}

function renderSystemError(msg) {
    const errorBubble = document.createElement('div');
    errorBubble.className = 'chat-bubble assistant';
    errorBubble.style.borderColor = 'var(--danger)';
    errorBubble.style.backgroundColor = 'rgba(218, 55, 60, 0.08)';
    errorBubble.innerHTML = `<span style="color: var(--danger); font-weight: 600;">System Error:</span> ${msg}`;
    chatFeed.appendChild(errorBubble);
}

// Log utility
function logToConsole(text, type = 'system') {
    const line = document.createElement('div');
    line.className = `log-line ${type}`;
    
    const time = new Date().toLocaleTimeString();
    line.textContent = `[${time}] ${text}`;
    
    logsConsole.appendChild(line);
    logsConsole.scrollTop = logsConsole.scrollHeight;
}

// Reset functions
function resetPipeline() {
    document.querySelectorAll('.stage').forEach(el => {
        el.classList.remove('active', 'completed');
    });
}

function clearTelemetry() {
    statModel.textContent = '-';
    statTokens.textContent = '-';
    statElapsed.textContent = '-';
    statMemory.textContent = '-';
}
