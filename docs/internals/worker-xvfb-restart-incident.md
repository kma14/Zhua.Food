# Worker 抓取中断故障报告（2026-07-14）

## 结论

Worker 并没有停止调度。Quartz 从 2026-07-14 06:00 起仍按 06:00 / 18:00 执行，但七家门店的 Chromium 都在启动阶段失败，因此没有写入新价格。

直接故障是容器内没有可用的 X Server。高置信度根因是：同一个容器停止再启动后，Xvfb 留下了 `/tmp/.X99-lock` 或 `/tmp/.X11-unix/X99`；`entrypoint.sh` 再次启动 `:99` 时失败，但它把 Xvfb 错误丢到 `/dev/null`，等待结束后也没有检查显示是否就绪，仍然启动了 Worker。直到定时任务拉起 headed Chromium，日志才出现 `Missing X server or $DISPLAY`。

建议先 **recreate worker 容器** 恢复服务，不要只 restart；随后修复入口脚本并发布新镜像。

## 影响

- 最后一次完整成功抓取：2026-07-13 18:00-18:13（Pacific/Auckland）。
- 受影响时段：2026-07-14 06:00 至本报告生成时。
- 七家 active store 均受影响，每次结果均为 `Failed products=0 snapshots=0`。
- API、Postgres、Quartz、match 和 category 流程仍在运行；match/category 只是继续处理旧数据。
- 2026-07-17 20:25 实时查询 NAS `/stores`：七家的 `lastCrawledAt` 仍停在 2026-07-13 18:01-18:13，说明 7 月 17 日 18:00 这一轮也没有恢复。

## 已确认的证据

### NAS 日志时间线

| 时间（Pacific/Auckland） | 事件 |
|---|---|
| 2026-07-13 18:00 | `[scheduled] starting crawl of 7 active stores` |
| 2026-07-13 18:01-18:13 | 七家门店全部 `Succeeded` |
| 2026-07-14 05:35:50 | `Application is shutting down...` |
| 2026-07-14 05:35:51 | Quartz `Shutdown complete` |
| 2026-07-14 05:38:52 | Quartz 和应用重新启动 |
| 2026-07-14 06:00:02 | 调度正常触发七家门店抓取 |
| 2026-07-14 06:00:08 起 | Chromium 报 `Missing X server or $DISPLAY`，七家全部失败 |
| 之后每次 06:00 / 18:00 | 同一错误重复出现 |

这说明 05:35-05:38 的容器停止/启动是故障触发点；不是 Cron 漏跑，也不是数据库不可用。

### 容器与代码配置

- NAS 镜像：`ghcr.io/kma14/zhua-worker:6a34914326a7b990f39c9abbe8bc0c3d8a5b8adc`。
- Synology 导出配置没有覆盖 image 的 command；镜像应执行 `/app/entrypoint.sh`。
- `TZ=Pacific/Auckland`、`Crawl__Cron=0 0 6,18 * * ?` 和 `PLAYWRIGHT_BROWSERS_PATH=/ms-playwright` 均存在。
- `entrypoint.sh` 启动 `Xvfb :99`，设置 `DISPLAY=:99`，但：
  - Xvfb stdout/stderr 被重定向到 `/dev/null`；
  - `set -e` 无法捕获后台进程启动失败；
  - 等待 30 次后，无论 `xdpyinfo` 是否成功都会 `exec /app/Zhua.Worker`；
  - 启动前没有清理 display 99 的残留 lock/socket。
- Synology 导出显示 `enable_restart_policy=false`，与仓库 Compose 的 `restart: unless-stopped` 不一致。这不是本次浏览器失败的直接原因，但属于部署配置漂移。

### 本地复现

在现有 Worker 镜像的临时容器里预置 display 99 锁后启动 Xvfb，可以稳定复现：

```text
Fatal server error:
Server is already active for display 99
If this server is no longer running, remove /tmp/.X99-lock
and start again.
```

删除 `/tmp/.X99-lock` 和 `/tmp/.X11-unix/X99` 后，同一镜像中的 `xdpyinfo -display :99` 立即成功。该结果与 NAS 上“首次运行正常、容器重启后永久缺少 X Server”的表现完全一致。

## 根因链

1. Worker 使用 headed Chromium；真实 headless 模式会触发目标站点 WAF，所以容器必须依赖 Xvfb。
2. 2026-07-14 05:35 容器停止。Worker 作为 PID 1 收到关闭信号并优雅退出，后台 Xvfb 随容器被终止，可能没有机会删除 display 99 的 lock/socket。
3. 同一个容器再次启动时，可写层中的 `/tmp` 仍然存在。Xvfb 因 display 99 锁冲突而退出。
4. 入口脚本隐藏了 Xvfb 错误，也没有在显示未就绪时退出，因此 Worker/Quartz 显示为正常运行。
5. 06:00 调度触发时，Chromium 找不到 X Server，七家门店全部在浏览器启动阶段失败。

第 2 步中的 NAS 实际 lock/socket 状态无法直接读取：NAS 未开放 SSH（22）或 Docker API（2375）。但日志时间线、入口脚本缺陷和容器内复现共同构成高置信度证据。

## 立即恢复

只重建 Worker，不要删除 Postgres、数据库 volume 或 crawl archive：

```bash
docker compose up -d --force-recreate worker
```

Synology Container Manager 中应选择重新创建/重新部署项目里的 `worker`，而不是仅点击 Restart。`/app/crawl-archive` 已绑定到 NAS 路径，重建 Worker 不会删除归档；数据库也在独立容器/volume 中。

重建后先确认虚拟显示：

```bash
docker exec zhuafood-worker-1 sh -c 'xdpyinfo -display :99 >/dev/null 2>&1 && echo Xvfb-ready'
```

然后避开 06:00 / 18:00 调度窗口做一次手动 crawl，或观察下一次定时任务。成功标准是七家均不再出现 `Missing X server`，并产生新的 `LastCrawledAt` / snapshots。

## 永久修复

修改 `src/Zhua.Worker/entrypoint.sh`：

1. 启动前删除 display 99 的残留 lock/socket。
2. Xvfb 日志写入临时文件；正常时保持安静，失败时输出日志。
3. 同时检查 Xvfb 进程和 `xdpyinfo`；超时或进程退出时让容器启动失败，绝不能继续启动 Worker。
4. 保留 `DISPLAY=:99` 和 headed Chromium，不要改为 Playwright `Headless=true`。

建议实现形态：

```sh
#!/bin/sh
set -eu

display=:99
xvfb_log=/tmp/xvfb.log

rm -f /tmp/.X99-lock /tmp/.X11-unix/X99
Xvfb "$display" -screen 0 1920x1080x24 -nolisten tcp >"$xvfb_log" 2>&1 &
xvfb_pid=$!

i=0
while [ "$i" -lt 30 ]; do
  if xdpyinfo -display "$display" >/dev/null 2>&1; then
    export DISPLAY="$display"
    exec /app/Zhua.Worker "$@"
  fi

  if ! kill -0 "$xvfb_pid" 2>/dev/null; then
    break
  fi

  i=$((i + 1))
  sleep 0.2
done

echo "Xvfb failed to become ready on $display" >&2
cat "$xvfb_log" >&2
exit 1
```

同时把 NAS 实际 restart policy 对齐为 `unless-stopped`。入口脚本修好后，Xvfb 真失败会使容器退出，restart policy 才能发挥作用，而不是留下一个“运行中但永远抓不到数据”的假健康容器。

## 验证清单

- 新镜像首次启动：`xdpyinfo -display :99` 成功。
- 对同一个 Worker 容器执行 stop/start：`xdpyinfo` 仍成功。
- 连续执行两次 stop/start，确认没有 `.X99-lock` 回归。
- 手动 crawl 七家门店全部成功。
- 到下一次 06:00 / 18:00，Quartz 自动抓取成功并更新数据库时间。
- 人为让 Xvfb 启动失败时，容器应退出并输出 Xvfb 原始错误，不能进入 Quartz `Application started` 状态。
- Synology Container Manager 显示 Worker restart policy 已启用。

## 仍需后端/运维确认

Worker 日志只能证明 05:35 收到了正常关闭信号，不能说明是谁触发了停止。请在 Synology Log Center、Container Manager 项目事件和计划任务中检查 2026-07-14 05:35 附近是否有：NAS 重启、Container Manager 更新、项目重新部署、镜像更新任务或人工 stop/start。

这个外部触发原因需要记录，但即使再次发生，修复后的入口脚本也应能安全恢复。

## Decision log

- 2026-07-17 20:24 🧑‍⚖️ 根据用户要求完成只读诊断并整理后端修复报告；未修改 Worker/后端实现。
- 2026-07-19 00:00 🧑‍⚖️ 按本文档"永久修复"方案改写 `src/Zhua.Worker/entrypoint.sh`：启动前清理 display 99 残留 lock/socket；Xvfb 输出改写入 `/tmp/xvfb.log`；就绪则在循环内 `export DISPLAY` + `exec` Worker，Xvfb 进程死亡或超时则输出日志并 `exit 1`（容器失败，绝不带病启动 Worker）。已在本地 `zhuafood-worker:latest` 镜像里用 stub Worker 验证三个场景：全新启动 ✓、预置残留 lock/socket（本次故障场景）✓、Xvfb 无法启动 → 容器 exit 1 并回显 Xvfb 原始错误 ✓。NAS 侧仍需：发布新镜像 + 重建 worker 容器 + 把 restart policy 对齐为 `unless-stopped`。
