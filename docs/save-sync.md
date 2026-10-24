# 存档同步协议（移动端适配指南）

TipCL 电脑端提供一个局域网同步服务，手机上的 Java 版启动器按本文实现客户端，就能和电脑互传存档。
电脑端负责：被发现、配对、列出世界、打包下载、接收上传（覆盖前自动备份）。
手机端负责：发现电脑、让玩家输入配对码、算本地世界指纹、决定推还是拉、处理冲突。

- 协议版本：`1`
- 只支持 **Java 版存档**（Anvil 格式），基岩版不在范围内
- 只在局域网内工作，电脑端拒绝公网来源的连接

---

## 1. 电脑端怎么打开

**设置 → 手机同步 → 局域网同步** 打开开关，下面会显示：

```
配对码 482913 · 192.168.1.5:25586 · 已配对设备 0
```

也可以直接对 TipCL 的 AI 说"把存档同步到手机"，它会打开服务并告诉玩家配对码。

首次打开时，Windows 防火墙可能会弹窗，需要勾选 **专用网络**。手机端连不上时，优先提示玩家检查这一项。

| 端口 | 协议 | 用途 |
|---|---|---|
| 25586 | TCP / HTTP | 同步接口（端口被占用时以设置页显示的为准） |
| 25587 | UDP | 局域网发现 |

---

## 2. 发现电脑

手机向 `255.255.255.255:25587` 发送 UDP 广播，内容是 ASCII 字符串：

```
TIPCL_SYNC_DISCOVER
```

每台开着同步的电脑会回一个 UDP 包（UTF-8 JSON），发往手机的来源地址：

```json
{ "service": "tipcl-sync", "protocol": 1, "server_id": "9f2c…", "server_name": "DESKTOP-ABC", "port": 25586 }
```

- 电脑的 IP 取 UDP 回包的来源地址，HTTP 端口取 `port`
- 建议每秒广播一次，持续 3 到 5 秒，按 `server_id` 去重后列出
- Android 上收广播回包前要先拿 `WifiManager.MulticastLock`（需要 `CHANGE_WIFI_MULTICAST_STATE` 权限）
- 部分路由器开启了 AP 隔离、会拦截广播，所以必须提供**手动输入地址**（`IP:端口`）作为兜底

---

## 3. HTTP 接口

基础路径：`http://<电脑IP>:<端口>/api/sync/v1`

- 除 `/info` 和 `POST /pair` 外，都要带请求头 `Authorization: Bearer <token>`
- 每个请求一个连接，服务端返回 `Connection: close`
- 上传必须带 `Content-Length`，不支持 chunked。HttpURLConnection 用 `setFixedLengthStreamingMode(size)`
- 路径里的世界名、查询参数一律做 URL 编码（UTF-8）

### 3.1 错误格式

非 2xx 时返回 JSON：

```json
{ "error": "locked", "message": "电脑上正在玩这个存档，退出世界后再同步" }
```

`message` 可以直接展示给玩家。

| 状态码 | error | 含义 | 手机端怎么处理 |
|---|---|---|---|
| 400 | bad_request / bad_name / bad_zip | 参数或压缩包不对 | 提示 message |
| 401 | unauthorized | 没配对或令牌被清除 | 删除本地令牌，重新配对 |
| 403 | bad_code | 配对码错误 | 让玩家重新输入；连错 5 次电脑会换码 |
| 404 | no_target / no_world / not_found | 实例或世界不存在 | 刷新列表 |
| 409 | conflict | 上传时发现电脑端也改过 | 进入冲突处理（第 5 节） |
| 411 | length_required | 缺少 Content-Length | 实现问题 |
| 413 | too_large | 超过 4 GB | 提示 |
| 423 | locked | 电脑上正在玩这个世界 | 提示玩家在电脑上退出世界 |

### 3.2 `GET /info`（不需要令牌）

```json
{ "service": "tipcl-sync", "protocol": 1, "server_id": "9f2c…", "server_name": "DESKTOP-ABC", "app": "TipCL/3.2.0" }
```

手动输入地址时，先调用它确认对方是 TipCL；`protocol` 大于自己支持的版本时，提示玩家更新手机端。

### 3.3 `POST /pair`（不需要令牌）

请求：

```json
{ "code": "482913", "device_id": "手机端生成并持久保存的 UUID", "device_name": "Xiaomi 14" }
```

成功返回：

```json
{ "token": "随机令牌", "server_id": "9f2c…", "server_name": "DESKTOP-ABC" }
```

- 令牌长期有效，按 `server_id` 存在手机本地（建议放 EncryptedSharedPreferences），以后不用再配对
- 同一个 `device_id` 重新配对会替换旧令牌
- 配对成功后，电脑端会自动更换配对码

### 3.4 `DELETE /pair`

取消本机配对，令牌立即失效。返回 `{ "ok": true }`。

### 3.5 `GET /targets`

列出电脑上可以同步的位置。一个位置是一个实例，或者开了版本隔离的某个版本：

```json
{ "targets": [
  { "instance": "默认", "version": "", "label": "默认", "default": true, "worlds": 3 },
  { "instance": "默认", "version": "1.20.1-Fabric", "label": "默认 / 1.20.1-Fabric", "default": false, "worlds": 1 }
] }
```

第一次同步某个世界时让玩家选位置，默认选 `default: true` 的那个；之后记住映射关系（见第 6 节）。

### 3.6 `GET /worlds?instance=…&version=…`

```json
{ "instance": "默认", "version": "", "worlds": [
  { "name": "新的世界", "level_name": "新的世界", "last_played": 1729770000000, "data_version": 3465,
    "game_version": "1.20.1", "bytes": 10485760, "mtime": 1729770001000, "locked": false,
    "hash": "5aca8b7c…" }
] }
```

| 字段 | 说明 |
|---|---|
| name | 世界文件夹名，后续接口都用它 |
| level_name | level.dat 里的显示名 |
| last_played | level.dat 的 `Data.LastPlayed`，Unix 毫秒 |
| data_version | `Data.DataVersion`，世界格式版本 |
| game_version | `Data.Version.Name` |
| locked | 电脑上是否正在玩 |
| hash | 世界指纹（第 4 节） |

`instance` 留空表示电脑的默认实例；`version` 留空表示不隔离。

### 3.7 `GET /worlds/{name}?instance=…&version=…`

单个世界的信息，格式同上。

### 3.8 `GET /worlds/{name}/zip?instance=…&version=…`

下载世界。

- 响应体是 zip，顶层是 `{name}/` 文件夹，不包含 `session.lock`
- 响应头 `X-TipCL-World-Hash` 是打包时的指纹，解压完成后把它存为该世界的 `base`
- 电脑上正在玩这个世界时返回 423

### 3.9 `PUT /worlds/{name}/zip?instance=…&version=…&base=<hash>[&force=1]`

上传世界，请求体是 zip。

- zip 里的 `level.dat` 必须在根目录，或在唯一的顶层文件夹里，推荐和下载一样用 `{name}/…`
- 不要打包 `session.lock`
- `base`：手机记录的上次同步指纹。电脑端会比较自己当前的指纹：
  - 相同：接受，先备份电脑上的旧世界，再覆盖
  - 不同：返回 409，响应体带电脑端当前世界信息 `world`（以及 `exists`）
  - 电脑上没有这个世界、且 `base` 为空：直接创建
- `force=1`：跳过比较，强制覆盖（电脑端仍然会先备份）
- 成功返回导入后的世界信息，并多两个字段：

```json
{ "name": "新的世界", "hash": "…", "backup": "新的世界-20261024-221530.zip", "received": "新的世界-20261024-221530-1.zip", "…": "…" }
```

把返回的 `hash` 存为新的 `base`。手机上传的每个版本都会留在电脑的备份目录里，玩家可以在 TipCL 里随时回滚。

---

## 4. 世界指纹（两端必须完全一致）

```
对世界文件夹里的每个普通文件（递归，跳过根目录下的 session.lock）：
    rel  = 相对世界文件夹的路径，分隔符统一为 "/"
    line = rel + "\t" + 文件字节数(十进制) + "\t" + sha256(文件内容)小写十六进制 + "\n"
按 rel 排序：UTF-16 码元顺序，即 Java/Kotlin 的 String.compareTo，或 C# 的 string.CompareOrdinal
manifest = 所有 line 依次拼接
hash     = sha256(manifest 的 UTF-8 字节) 的小写十六进制
```

- 空文件夹不参与计算
- 只跳过根目录的 `session.lock`，子目录里的同名文件照常计算
- 不要按文件修改时间判断，各端解压后时间会变
- 大世界算一次比较慢，可以按 (路径, 大小, 修改时间) 缓存单个文件的 sha256。但刚写过几秒内的文件不要用缓存

Kotlin 参考实现：

```kotlin
import java.io.File
import java.security.MessageDigest

fun sha256Hex(bytes: ByteArray): String =
    MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }

fun fileSha256(f: File): String {
    val md = MessageDigest.getInstance("SHA-256")
    f.inputStream().use { input ->
        val buf = ByteArray(1 shl 16)
        while (true) {
            val n = input.read(buf)
            if (n <= 0) break
            md.update(buf, 0, n)
        }
    }
    return md.digest().joinToString("") { "%02x".format(it) }
}

fun worldHash(worldDir: File): String {
    val root = worldDir.canonicalFile
    val files = root.walkTopDown().filter { it.isFile }
        .map { it.relativeTo(root).invariantSeparatorsPath to it }
        .filter { it.first != "session.lock" }
        .sortedWith { a, b -> a.first.compareTo(b.first) }
    val sb = StringBuilder()
    for ((rel, f) in files) {
        sb.append(rel).append('\t').append(f.length()).append('\t').append(fileSha256(f)).append('\n')
    }
    return sha256Hex(sb.toString().toByteArray(Charsets.UTF_8))
}
```

自检用例：一个文件夹里有 `a.txt`（内容 `x`）、`B.txt`（内容 `y`）、`b/c.txt`（空文件），manifest 必须是：

```
B.txt	1	a1fce4363854ff888cff4b8e7875d600c2682390412a8cf79b37d0b11148b0fa
a.txt	1	2d711642b726b04401627ca9fbac32f5c8530fb1903cc4db02258717921a4881
b/c.txt	0	e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
```

（注意 `B` 排在 `a` 前面。）电脑端的单元测试用的是同一个用例。

---

## 5. 同步决策

手机端对每个世界记录 `base`：上次两端同步成功时的指纹。拿到 `local`（手机现算）、`remote`（`/worlds` 返回的 hash）后：

| 条件 | 结果 | 动作 |
|---|---|---|
| local == remote | 一致 | 什么都不做，把 base 更新为它 |
| 只有手机有 | 只在本地 | 询问是否上传（base 传空） |
| 只有电脑有 | 只在电脑 | 询问是否下载 |
| remote == base | 只有手机改过 | 上传，`base=base` |
| local == base | 只有电脑改过 | 下载 |
| 其他（含从没同步过） | 冲突 | 让玩家选 |

冲突时给玩家看两边的 `level_name`、`last_played`（转成本地时间）、`game_version`，提供三个选项：

1. **用手机的**：上传并带 `force=1`。电脑端会先备份
2. **用电脑的**：手机端先把本地世界备份成 zip，再下载覆盖
3. **都保留**：把其中一份改名（例如 `新的世界 (手机)`），作为新世界上传或下载

电脑端的 C# 实现在 `SaveSync.Decide`，规则和这张表一致。

---

## 6. 手机端需要保存的状态

建议存成一个 JSON 文件或一张表：

```json
{
  "device_id": "UUID，首次启动生成",
  "servers": {
    "<server_id>": {
      "name": "DESKTOP-ABC",
      "last_address": "192.168.1.5:25586",
      "token": "…",
      "worlds": {
        "<手机本地世界的绝对路径或 实例/世界名>": {
          "instance": "默认", "version": "", "remote_name": "新的世界", "base": "5aca8b7c…"
        }
      }
    }
  }
}
```

- 电脑 IP 可能会变：优先用广播重新发现，用 `server_id` 认出同一台电脑；发现不了再尝试 `last_address`
- 手机端的世界名和电脑端可以不同，以映射为准

---

## 7. 安全与注意事项

- 同步前检查手机本地世界是否正在运行。游戏在跑时不要打包，也不要覆盖
- 覆盖手机本地世界前，先打一份备份。电脑端已经这样做了，两端保持一致
- 解压电脑发来的 zip 时，校验每个条目的路径：拒绝绝对路径、`..` 和盘符，防止写到存档目录之外
- 先解压到临时目录，成功后再整体替换。不要边解压边覆盖
- `data_version` 比手机端游戏版本更高时，要提醒玩家：用旧版本打开新存档可能会损坏。用 `game_version` 显示具体版本
- 模组世界：两端模组不一致时，缺少的模组方块会被删除。可以在界面上提醒"请确保两端模组一致"
- 令牌等同于电脑存档的写权限，不要写进日志

---

## 8. 推荐的手机端流程

```
打开「同步到电脑」
 ├─ 有已配对电脑 → 广播发现 → 找到对应 server_id → 用令牌调用 /worlds
 └─ 没有 → 广播发现列出电脑（或手动输入地址）→ 输入配对码 → POST /pair → 保存令牌
选世界 → 算 local hash → 按第 5 节决策
 ├─ 上传：打包 zip（不含 session.lock）→ PUT …/zip?base=… → 成功后保存返回的 hash 为 base
 ├─ 下载：GET …/zip → 备份本地 → 解压到临时目录 → 替换 → base = X-TipCL-World-Hash
 └─ 冲突：让玩家选（第 5 节）
遇到 401 → 清除令牌重新配对；遇到 423 → 提示在电脑上退出世界
```

用 curl 手动联调：

```bash
curl http://192.168.1.5:25586/api/sync/v1/info
curl -X POST -d '{"code":"482913","device_id":"test","device_name":"curl"}' http://192.168.1.5:25586/api/sync/v1/pair
curl -H "Authorization: Bearer <token>" http://192.168.1.5:25586/api/sync/v1/worlds
curl -H "Authorization: Bearer <token>" -o w.zip "http://192.168.1.5:25586/api/sync/v1/worlds/%E6%96%B0%E7%9A%84%E4%B8%96%E7%95%8C/zip"
curl -X PUT -H "Authorization: Bearer <token>" --data-binary @w.zip "http://192.168.1.5:25586/api/sync/v1/worlds/%E6%96%B0%E7%9A%84%E4%B8%96%E7%95%8C/zip?base=<hash>"
```
