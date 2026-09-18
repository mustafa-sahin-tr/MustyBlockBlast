import json
import subprocess
import time
import sys

UVX_PATH = "/Users/mustafasahin/.local/bin/uvx"
CMD = [
    UVX_PATH,
    "--offline",
    "--from",
    "mcpforunityserver==10.2.0",
    "mcp-for-unity",
    "--transport",
    "stdio"
]

def send_rpc(proc, req_id, method, params=None):
    payload = {
        "jsonrpc": "2.0",
        "id": req_id,
        "method": method,
        "params": params or {}
    }
    raw = json.dumps(payload) + "\n"
    proc.stdin.write(raw.encode("utf-8"))
    proc.stdin.flush()

    while True:
        line = proc.stdout.readline().decode("utf-8").strip()
        if not line:
            continue
        try:
            res = json.loads(line)
            if res.get("id") == req_id:
                return res
        except json.JSONDecodeError:
            continue

def main():
    print("🚀 MCP Sunucusu başlatılıyor...")
    proc = subprocess.Popen(
        CMD,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE
    )

    req_id = 1

    # 1. Initialize
    print("🤝 MCP Handshake yapılıyor...")
    send_rpc(proc, req_id, "initialize", {
        "protocolVersion": "2024-11-05",
        "capabilities": {},
        "clientInfo": {"name": "auto-builder", "version": "1.0"}
    })
    req_id += 1

    # 2. Build Başlat
    print("📦 iOS Build tetikleniyor...")
    build_res = send_rpc(proc, req_id, "tools/call", {
        "name": "manage_build",
        "arguments": {
            "action": "build",
            "target": "iOS",
            "outputPath": "Builds/iOS"
        }
    })
    req_id += 1

    # Dönen yanıtı parse et
    structured = build_res.get("result", {}).get("structuredContent", {})
    job_id = structured.get("data", {}).get("job_id")
    poll_interval = structured.get("_mcp_poll_interval", 5.0)

    print(f"⏳ Build kuyruğa alındı (Job: {job_id}). Durum sorgulanıyor...")

    # 3. Status Polling Döngüsü
    while True:
        time.sleep(poll_interval)
        status_res = send_rpc(proc, req_id, "tools/call", {
            "name": "manage_build",
            "arguments": {
                "action": "status",
                "job_id": job_id
            }
        })
        req_id += 1

        content = status_res.get("result", {}).get("structuredContent", {})
        status = content.get("_mcp_status") or content.get("status")

        if status == "pending" or status == "in_progress":
            print(f"⌛ Derleme devam ediyor... ({time.strftime('%H:%M:%S')})")
            continue
        elif content.get("success") is True or status == "completed":
            print("\n🎉 Build başarıyla tamamlandı!")
            output_path = content.get("data", {}).get("output_path", "Builds/iOS")
            print(f"📍 Xcode Proje Yolu: {output_path}")
            print(f"👉 Sonraki adım: open {output_path}/Unity-iPhone.xcworkspace")
            break
        else:
            print("\n❌ Build başarısız oldu veya hata verdi:")
            print(json.dumps(content, indent=2))
            break

    proc.terminate()

if __name__ == "__main__":
    main()