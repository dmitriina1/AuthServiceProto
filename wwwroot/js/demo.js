"use strict";

const clients = {
    orders: {
        clientId: "orders-client",
        scope: "openid profile email offline_access orders_api",
        api: "/api/orders/me"
    },
    profile: {
        clientId: "profile-client",
        scope: "openid profile email offline_access profile_api",
        api: "/api/profile/me"
    }
};

const statusBox = document.getElementById("status");
const resultBox = document.getElementById("result");
const redirectUri = `${window.location.origin}/demo`;

document.querySelectorAll("[data-client]").forEach(button => {
    button.addEventListener("click", () => startAuthorization(button.dataset.client));
});

async function startAuthorization(clientName) {
    const client = clients[clientName];
    const verifier = randomBase64Url(64);
    const state = randomBase64Url(24);
    const challenge = await sha256Base64Url(verifier);

    sessionStorage.setItem("pkce-demo", JSON.stringify({ clientName, verifier, state }));

    const query = new URLSearchParams({
        client_id: client.clientId,
        redirect_uri: redirectUri,
        response_type: "code",
        scope: client.scope,
        state,
        code_challenge: challenge,
        code_challenge_method: "S256"
    });

    window.location.assign(`/connect/authorize?${query}`);
}

async function finishAuthorization() {
    const query = new URLSearchParams(window.location.search);
    const code = query.get("code");
    if (!code) return;

    const saved = JSON.parse(sessionStorage.getItem("pkce-demo") || "null");
    if (!saved || saved.state !== query.get("state")) {
        throw new Error("OAuth state не совпадает: возможен устаревший или подменённый callback.");
    }

    const client = clients[saved.clientName];
    statusBox.textContent = `Обмениваем code для ${client.clientId}...`;

    const tokenResponse = await fetch("/connect/token", {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: new URLSearchParams({
            grant_type: "authorization_code",
            client_id: client.clientId,
            redirect_uri: redirectUri,
            code,
            code_verifier: saved.verifier
        })
    });
    const tokens = await tokenResponse.json();
    if (!tokenResponse.ok) throw new Error(JSON.stringify(tokens));

    const apiResponse = await fetch(client.api, {
        headers: { Authorization: `Bearer ${tokens.access_token}` }
    });
    const api = await apiResponse.json();

    sessionStorage.removeItem("pkce-demo");
    history.replaceState({}, "", "/demo");
    statusBox.className = "alert alert-success";
    statusBox.textContent = `${client.clientId}: токен получен, API успешно вызван.`;
    resultBox.value = JSON.stringify({ tokens, api }, null, 2);
}

function randomBase64Url(length) {
    const bytes = crypto.getRandomValues(new Uint8Array(length));
    return base64Url(bytes);
}

async function sha256Base64Url(value) {
    const bytes = new TextEncoder().encode(value);
    return base64Url(new Uint8Array(await crypto.subtle.digest("SHA-256", bytes)));
}

function base64Url(bytes) {
    return btoa(String.fromCharCode(...bytes))
        .replaceAll("+", "-")
        .replaceAll("/", "_")
        .replaceAll("=", "");
}

finishAuthorization().catch(error => {
    statusBox.className = "alert alert-danger";
    statusBox.textContent = "Авторизация завершилась ошибкой.";
    resultBox.value = error.stack || error.message;
});
