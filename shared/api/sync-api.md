# 司南 · 局域网同步 API 契约（唯一真源）

> 桌面端 `Sinan.App/Services/SyncServer.cs` 与手机端 `mobile/lib/data/sync_client.dart`
> 都按本文件实现，**不各写一份协议**。改协议先改这里，再同时改两端。

## 1. 传输与鉴权

| 项 | 约定 |
|---|---|
| 协议 | HTTP/1.1，JSON UTF-8（附件上传为原始字节） |
| 地址 | `http://<桌面端局域网 IP>:5180`，仅监听局域网，无 TLS |
| 鉴权 | `Authorization: Bearer <token>`；`/api/ping` 与 `/api/pair` 除外 |
| token | 配对时一次性下发，手机端存本机；桌面端只存 `SHA-256(token)` |
| 错误 | 统一 `{"ok":false,"error":"<code>","message":"<中文可读>"}` + 4xx/5xx |
| 幂等 | 上行操作带 `clientOpId`，桌面端 `sync_receipts` 去重，重发返回上次结果 |
| 版本 | 所有可同步实体带 `version`；上行必须带 `baseVersion`，不一致即冲突 |

服务端只监听局域网，不做公网穿透、不做 HTTPS。手机端 Android 清单已允许明文 HTTP。

## 2. 端点一览

| 方法 | 路径 | 鉴权 | 用途 |
|---|---|---|---|
| GET | `/api/ping` | 否 | 探测地址、读取服务名与协议版本 |
| POST | `/api/pair` | 否 | 用 6 位配对码换 deviceId + token |
| POST | `/api/sync/push` | 是 | 上行一批业务操作（新建/阶段/状态/评论） |
| GET | `/api/sync/pull?cursor=N&limit=200` | 是 | 按 `change_log.seq` 拉增量 |
| POST | `/api/attachments/{issueId}/{attachmentId}` | 是 | 上传一张照片的原始字节 |
| GET | `/api/attachments/{attachmentId}` | 是 | 下载附件字节（当前手机端不下行照片） |

## 3. GET /api/ping

响应 200：

```json
{
  "ok": true,
  "app": "sinan",
  "protocol": 1,
  "serverName": "司南 · 桌面端",
  "desktopVersion": "0.0.7",
  "time": "2026-11-26T09:41:00.000Z"
}
```

手机端用 `app == "sinan"` 与 `protocol == 1` 判断地址是否为本系统服务，
不把任意 HTTP 200 当成"已连接"。

## 4. POST /api/pair

请求：

```json
{
  "code": "824716",
  "deviceName": "Pixel 7",
  "platform": "android",
  "employeeNo": "engineer"
}
```

- `code`：桌面端设置页生成的 6 位数字，10 分钟有效、一次性。
- `deviceName`：手机型号或用户可改的设备名，用于桌面端设备列表。
- `employeeNo`：手机端登录账号（`engineer` / `admin`），写入设备记录供审计。
- `platform`：`android` / `ios`。

成功 200：

```json
{
  "ok": true,
  "deviceId": "DEV-9f2c...",
  "token": "v1_...",
  "serverName": "司南 · 桌面端",
  "protocol": 1,
  "pairedAt": "2026-11-26T09:41:00.000Z"
}
```

失败 400：`pair_code_invalid`（不存在/已用/过期）、`pair_code_expired`。
失败 409：`device_limit`（当前最多 8 台，需在桌面端先移除旧设备）。

### 4.1 二维码配对载荷（桌面端生成 / 手机端扫描）

桌面端在生成 6 位配对码后，把「服务器地址 + 配对码」编码成二维码；手机端
「同步中心 → 扫码配对」扫描后直接调用 `POST /api/pair`，不需要手输地址和数字码。

二维码内容固定为 URI（UTF-8）：

```
sinan://pair?v=1&url=http%3A%2F%2F192.168.0.89%3A5180&code=824716
```

| 参数 | 必填 | 说明 |
|---|---|---|
| `v` | 是 | 载荷版本，当前 `1`；手机端不认识的版本直接提示"桌面端版本不兼容" |
| `url` | 是 | 桌面端监听地址，URL 编码；等价于手机端手输的 `192.168.0.89:5180` |
| `code` | 是 | 6 位一次性配对码，10 分钟有效、只可使用一次 |

约束：

- 二维码只承载**地址与一次性码**，**不含 token**；token 仍只在 `/api/pair` 成功响应里下发。
- 配对码语义、有效期、设备上限与第 4 节完全一致，二维码不是第二套配对机制。
- 手机端扫码后仍先 `GET /api/ping` 校验 `app=sinan` / `protocol=1`，再调用 `/api/pair`。
- 手输地址 + 配对码作为兜底保留，扫码失败时仍可手动完成配对。

## 5. POST /api/sync/push

请求：

```json
{
  "operations": [
    {
      "clientOpId": 123,
      "entity": "issue",
      "entityId": "local-1732600000000000",
      "op": "create",
      "baseVersion": 0,
      "clientTime": "2026-11-26T09:38:00.000Z",
      "payload": {
        "seq_no": "L001",
        "machine_code": "P7391106",
        "title": "夹爪等待超时",
        "solution": "",
        "status": "装配中",
        "stage_code": "assembly",
        "owner_text": "张工",
        "due_raw": "11.28"
      }
    }
  ]
}
```

支持的操作：

| entity | op | payload | 说明 |
|---|---|---|---|
| `issue` | `create` | 见上 | 手机新建问题；桌面端重编 `seq_no`（L001 占位作废） |
| `issue_stage` | `upsert` | `owner_text` / `due_raw` / `due_date` / `state` / `actual_date` / `remark` | `entityId` 形如 `<issueId>:<stageCode>` |
| `issue` | `status` | `{"status":"设计中"}` | 人工覆盖状态 |
| `issue` | `comment` | `{"author":"张工","body":"..."}` | 追加现场评论 |
| `attachment` | `upload` | — | **不放进 push**，由手机端单独走附件端点 |

响应 200：

```json
{
  "ok": true,
  "results": [
    {
      "clientOpId": 123,
      "status": "applied",
      "serverId": "local-1732600000000000",
      "serverVersion": 1,
      "seqNo": "037",
      "message": ""
    },
    {
      "clientOpId": 124,
      "status": "conflict",
      "serverVersion": 4,
      "serverPayload": { "owner_text": "李工", "due_raw": "11.30", "version": 4 },
      "message": "阶段已被桌面端修改"
    }
  ]
}
```

`status`：

- `applied`：已写入，`serverVersion` 是最新版本；手机端更新本地 version。
- `conflict`：`baseVersion` 落后，`serverPayload` 是桌面端当前值；**手机端必须人工选一边**，
  不得静默覆盖。
- `rejected`：数据不合法（问题不存在、机台号为空等），`message` 说明原因，操作留在队列。
- `duplicate`：同一 `clientOpId` 已处理过，返回上次结果（内容同 applied/conflict）。

冲突语义：同一问题两端都改 → 只有手机端会收到 `conflict`（桌面端是主库，先写先赢）；
手机端展示"保留手机 / 保留桌面"：
- 保留手机：用 `serverVersion` 作为新的 `baseVersion` 重发；
- 保留桌面：用 `serverPayload` 覆盖本地，操作标记已结算。

## 6. GET /api/sync/pull

请求：`GET /api/sync/pull?cursor=0&limit=200`（`cursor` 是上次响应里的 `cursor`）。

响应 200：

```json
{
  "ok": true,
  "cursor": 128,
  "hasMore": false,
  "changes": [
    {
      "seq": 127,
      "entity": "issue",
      "entityId": "ISS-0001",
      "op": "upsert",
      "createdAt": "2026-11-26T09:40:00.000Z",
      "payload": {
        "id": "ISS-0001",
        "seqNo": "001",
        "machineCode": "P7391101",
        "title": "……",
        "status": "设计中",
        "version": 5,
        "stages": [ { "code": "plan", "ownerText": "张工", "dueRaw": "11.20", "state": "done", "version": 2 } ]
      }
    }
  ]
}
```

- `payload` 是桌面端 `Issue` 的 camelCase JSON（含 `stages`、`attachments` 元数据）。
- `op = delete` 时 `payload` 只含 `id` 与 `deletedAt`。
- 服务端排除本设备自己推送产生的变更，避免回声。
- 手机端按 `cursor` 持久化；断网重来不丢增量。
- 手机端本地存在同一实体的未同步操作时，不直接覆盖，写本地 `conflicts` 表等人工选择。

## 7. 附件

### 7.1 上传

`POST /api/attachments/{issueId}/{attachmentId}?fileName=IMG_2841.jpg&sha256=<hex>`

- Body：原始 JPEG/PNG 字节（`Content-Type: image/jpeg`），单张上限 8 MB。
- `attachmentId` 由手机端生成（`att-<微秒>`），桌面端按此幂等：重复上传返回第一次结果。
- 桌面端落盘 `attachments/{issueId}/{attachmentId}.jpg`，登记 `attachments` 表并写
  `change_log`（entity=`attachment`），供其他设备更新照片计数。

成功 200：

```json
{ "ok": true, "attachmentId": "att-...", "storedPath": "<issueId>/att-....jpg", "size": 823145 }
```

失败：`attachment_too_large` / `issue_not_found` / `sha256_mismatch`。

### 7.2 下载（预留）

`GET /api/attachments/{attachmentId}` 返回文件字节。当前手机端不主动下行照片：
桌面端是主库，现场照片以手机上传为主；后续做"换机恢复"时再接。

## 8. 桌面端监听与防火墙

- 默认端口 `5180`，可在设置页看到实际监听地址；端口被占用时服务不启动并如实提示。
- Windows 首次监听会弹防火墙授权；拒绝后手机连不上，设置页会显示"服务未运行"，
  不伪造在线状态。
- 服务只绑定局域网 IPv4，不监听公网；外网访问不属于 MVP。

## 9. 版本演进

`protocol` 目前为 `1`。破坏性变更（字段语义、鉴权方式）必须升 `protocol`，
手机端在配对与 ping 时校验，不匹配就明确提示"桌面端版本过旧/过新"，不尝试兼容。
