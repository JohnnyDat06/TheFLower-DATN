# Final Boss Room Plan

## Mục tiêu

`Final_Boss_Room` là encounter cuối độc lập. Server điều phối trạng thái boss, hồi sinh, cứu đồng đội, wipe và reset; client chỉ gửi yêu cầu hợp lệ và hiển thị trạng thái.

## Trạng thái đã kiểm tra — 06/08/2026

### Đã hoàn thành

- [x] Scene `Final_Boss_Room` đã có trong Build Settings và hằng `Constants.Scenes.BOSS_FINAL`.
- [x] Cổng `CoopSceneTeleporter` ở Map 2 trỏ tới `Final_Boss_Room`.
- [x] `PlayerSpawner` đặt hai player vào `P1/P2`; khi vào boss room, server hồi đầy HP để không mang máu bị mất từ Map 2 sang attempt mới.
- [x] Loading overlay được yêu cầu ẩn `To Be Continued` ngay khi bắt đầu tải và một lần nữa khi tải xong.
- [x] Đã tạo hierarchy nền `BOSS ENCOUNTER`, `BossRespawnPoint`, `BossCenter`, `PlayerEntry` và `BossArena`; các tool test/checkpoint thừa đã được dọn.
- [x] Đã tạo khung mã `BossEncounterManager`, `BossRespawnPolicy`, `SOBossEncounterConfig` và state: `WaitingForPlayers`, `Intro`, `Active`, `WipeReset`, `Victory`.
- [x] `PlayerHealth` phát death event phía server và có API hồi máu server-authoritative.

### Đã hoàn thành trong hệ thống encounter

- [x] `BOSS ENCOUNTER` đã có `NetworkObject`; manager/policy được spawn bởi NGO cùng scene.
- [x] Đã tạo và gán `FinalBossEncounterConfig` (intro 2s, auto-respawn 10s, giữ revive 5s, revive 60%, phạm vi 3m, wipe 2s).
- [x] `BossEntryTrigger` và đăng ký từ `PlayerSpawner` yêu cầu đủ player trước intro; spawn registration giúp flow không phụ thuộc vào callback trigger sau teleport.
- [x] Revive dùng input Interact giữ liên tục, request/cancel RPC và server kiểm tra sender, HP chết, khoảng cách và state encounter mỗi frame.
- [x] Player chết được hồi trực tiếp lên 60% hoặc 100% và FSM owner đi qua `Respawning` rồi `Idle`.
- [x] `RespawnManager` thường bỏ qua Boss Room, nên không chạy đồng thời với policy boss.
- [x] `BossEncounterHUD` dựng Canvas runtime cho objective, countdown hồi sinh, tiến trình cứu và trạng thái wipe/victory; `PlayerHealthHUDRemake` nay cũng chạy ở Boss Room để dùng cùng HUD HP Map 1–2.

### Còn lại cho gameplay boss

- [ ] `_resetTargets` đang trống; cần đăng ký boss, projectile, sàn/môi trường và cửa sau khi art/gameplay prefab được dựng.
- [ ] Chưa có prefab boss, projectile tầm xa, sàn sập hay cơ chế/vật phẩm gây sát thương gián tiếp.
- [ ] `CompleteEncounterServer` đã có state `Victory`, nhưng chưa có nguồn damage/cơ chế gọi nó hoặc scene kết thúc sau chiến thắng.

## Luồng mục tiêu

1. Cuối Map 2, cả hai player vào `CoopSceneTeleporter`; server load `Final_Boss_Room` qua NGO.
2. `PlayerSpawner` teleport hai player vào `P1/P2`, hồi 100% HP và đóng loading overlay.
3. `BossEncounterManager` chờ đủ hai player vào `PlayerEntry`, khóa cửa, chạy intro rồi chuyển `Active`.
4. Một player chết: server bắt đầu countdown 10 giây. Đồng đội giữ Interact trong 5 giây, trong phạm vi cho phép, để hồi ngay tại chỗ với 60% MaxHealth.
5. Hai player cùng chết: server hủy countdown/revive, dọn projectile, reset boss/sàn/môi trường, đưa cả hai về spawn và hồi 100% HP.
6. Khi boss bị hạ bằng cơ chế arena: server chuyển `Victory`, vô hiệu hóa nguy hiểm và chạy flow kết thúc.

## Thứ tự triển khai còn lại

1. **Boss và arena**
   - Tạo boss prefab đứng giữa phòng, tấn công tầm xa server-authoritative.
   - Tạo sàn sập/mục tiêu môi trường và vật phẩm/cơ chế gây sát thương gián tiếp.
   - Đăng ký projectile, sàn và boss vào reset targets; triển khai điều kiện thắng.

2. **Kiểm thử Host + Client**
   - Load Map 2 → Boss: không còn `To Be Continued`, cả hai xuất hiện đúng P1/P2 với đầy HP.
   - Chết đơn, auto-respawn 10 giây, revive thành công 60%, hủy revive khi rời xa/chết.
   - Chết đồng thời, wipe/reset; thử lại encounter và victory.
   - Kiểm tra Unity Console, EditMode/PlayMode tests và build.

## Tiêu chí hoàn thành

- Không có hai hệ thống hồi sinh cùng xử lý một player.
- Mọi state, hồi sinh, wipe, reset và victory đều do server quyết định và client không thể tự sửa HP/state.
- Wipe không để lại coroutine, projectile, trạng thái sàn hoặc HUD từ attempt cũ.
- Flow Host + Client qua Map 2 → Boss và attempt hoàn chỉnh không phát sinh lỗi Unity mới.
