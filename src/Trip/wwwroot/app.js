const form = document.querySelector("#trip-form");
const submitButton = document.querySelector("#submit-button");
const progress = document.querySelector("#progress");
const statusMessage = document.querySelector("#status");
const progressDetail = document.querySelector("#progress-detail");
const result = document.querySelector("#result");
const resumeButton = document.querySelector("#resume-button");
const keyInput = document.querySelector("#api-key");
const storageKey = "hermes-trip-job-id";
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const labels = {
    queued: "Požadavek je ve frontě.",
    running: "Hermes a specialisté připravují itinerář. Může to trvat několik minut.",
    completed: "Plán je hotový.",
    expired: "Plán už není k dispozici.",
    failed: "Požadavek se nepodařilo dokončit."
};
let polling = false;
let currentId = new URL(location.href).searchParams.get("trip");
try { currentId ||= localStorage.getItem(storageKey); }
catch { progressDetail.textContent = "Prohlížeč nepovoluje místní uložení ID; použij odkaz s ID v adrese."; }
if (!uuid.test(currentId || "")) currentId = null;
if (currentId) {
    progress.hidden = false;
    statusMessage.textContent = `ID ${currentId}: Vlož přístupový klíč a obnov sledování.`;
    resumeButton.hidden = false;
}

form.addEventListener("submit", async event => {
    event.preventDefault();
    if (polling) return;
    submitButton.disabled = true;
    progress.hidden = false;
    result.hidden = true;
    statusMessage.classList.remove("error");
    try {
        const response = await fetch("/api/trips", {
            method: "POST",
            headers: { authorization: `Bearer ${keyInput.value}`, "content-type": "application/json" },
            body: JSON.stringify({ requestText: document.querySelector("#request-text").value }),
            signal: AbortSignal.timeout(30_000)
        });
        const payload = await response.json();
        if (!response.ok) throw new Error(payload.error || "Zadání se nepodařilo odeslat.");
        if (!uuid.test(payload.id)) throw new Error("Server vrátil neplatné ID požadavku.");
        currentId = payload.id;
        try { localStorage.setItem(storageKey, currentId); }
        catch { progressDetail.textContent = "ID nelze uložit do prohlížeče; zachovej odkaz v adrese."; }
        history.replaceState(null, "", `?trip=${currentId}`);
        document.querySelector("#request-text").value = "";
        await pollStatus();
    } catch (error) { showError(error); }
});

resumeButton.addEventListener("click", async () => {
    if (!currentId || polling) return;
    if (keyInput.value.length < 32) {
        showError(new Error("Nejprve vlož přístupový klíč."));
        keyInput.focus();
        return;
    }
    await pollStatus();
});

async function pollStatus() {
    polling = true;
    submitButton.disabled = true;
    resumeButton.hidden = true;
    statusMessage.classList.remove("error");
    try {
        while (true) {
            const response = await fetch(`/api/trips/${currentId}`, {
                headers: { authorization: `Bearer ${keyInput.value}` },
                signal: AbortSignal.timeout(30_000)
            });
            const job = await response.json();
            if (!response.ok) throw new Error(job.error || "Stav nelze načíst. Obnov sledování; neposílej stejné zadání znovu.");
            statusMessage.textContent = `ID ${currentId}: ${labels[job.status] || job.status}`;
            progressDetail.textContent = job.progress || "";
            if (job.status === "completed") {
                result.textContent = job.result;
                result.hidden = false;
                return;
            }
            if (job.status === "failed" || job.status === "expired") {
                if (job.status === "failed") {
                    statusMessage.textContent += ` ${job.error || ""}`;
                    statusMessage.classList.add("error");
                }
                return;
            }
            await new Promise(resolve => setTimeout(resolve, 3_000));
        }
    } catch (error) { showError(error); }
    finally {
        polling = false;
        submitButton.disabled = false;
        resumeButton.hidden = !currentId;
    }
}

function showError(error) {
    statusMessage.textContent = `${currentId ? `ID ${currentId}: ` : ""}${error instanceof Error ? error.message : "Došlo k chybě."}`;
    statusMessage.classList.add("error");
    submitButton.disabled = false;
    resumeButton.hidden = !currentId;
}
