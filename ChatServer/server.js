const WebSocket = require('ws');
<<<<<<< HEAD

const PORT = 8081;
const wss = new WebSocket.Server({ port: PORT });

const rooms = new Map();

console.log(`[GlobalChat] サーバー起動: ws://localhost:${PORT}`);

wss.on('connection', (ws, req) => {
    const url = new URL(req.url, `https://catwalk-skimming-lapel.ngrok-free.dev`);
    const room = url.searchParams.get('room') ?? 'default';

    if (!rooms.has(room)) rooms.set(room, new Set());
    rooms.get(room).add(ws);

    const ip = req.socket.remoteAddress;
    console.log(`[+] 接続 room=${room} ip=${ip} (計${rooms.get(room).size}台)`);
=======
const PORT = 8081;
const wss = new WebSocket.Server({ port: PORT });
const allClients = new Set();

const Sep = '\x1E';
const KindChat = "CHAT";
const KindLink = "LINK";

wss.on('connection', (ws) => {
    allClients.add(ws);
    ws.myLinkId = null;
    ws.linkedIds = new Set();
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56

    ws.on('message', (data) => {
        const msg = data.toString().trim();
        if (!msg) return;

<<<<<<< HEAD
        console.log(`[msg] room=${room} : ${msg}`);

        let total = 0;
        for (const [, clients] of rooms) {
            for (const client of clients) {
                if (client.readyState === WebSocket.OPEN) {
                    client.send(msg);
                    total++;
                }
            }
        }
        console.log(`      → ${total}クライアントに送信`);
    });

    ws.on('close', () => {
        rooms.get(room)?.delete(ws);
        if (rooms.get(room)?.size === 0) rooms.delete(room);
        console.log(`[-] 切断 room=${room}`);
    });

    ws.on('error', (err) => {
        console.error(`[err] ${err.message}`);
    });
=======
        const parts = msg.split(Sep);
        if (parts.length < 2) return;

        const kind = parts[0];
        const fromId = parts[1];

        if (!ws.myLinkId && fromId) {
            for (const client of allClients) {
                if (client.myLinkId === fromId) {
                    ws.close();
                    return;
                }
            }
            ws.myLinkId = fromId;
        }

        if (ws.myLinkId !== fromId) return;

        if (kind === KindLink) {
            const toId = parts[2];
            if (!toId) return;

            ws.linkedIds.add(toId);

            for (const client of allClients) {
                if (client.myLinkId === toId && client.readyState === WebSocket.OPEN) {
                    client.send(msg);
                }
            }
        }
        else if (kind === KindChat) {
            for (const client of allClients) {
                if (client === ws) continue;

                if (ws.linkedIds.has(client.myLinkId) && client.linkedIds.has(ws.myLinkId)) {
                    if (client.readyState === WebSocket.OPEN) {
                        client.send(msg);
                    }
                }
            }
        }
    });

    ws.on('close', () => {
        allClients.delete(ws);
    });

    ws.on('error', () => { });
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
});