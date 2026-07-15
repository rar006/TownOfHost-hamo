const WebSocket = require('ws');

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

    ws.on('message', (data) => {
        const msg = data.toString().trim();
        if (!msg) return;

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
});