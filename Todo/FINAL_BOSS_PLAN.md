# Final Boss Room Plan

## Mục tiêu

Scene `Final_Boss_Room` là encounter cuối độc lập. Server điều phối toàn bộ trạng thái boss, hồi sinh, cứu đồng đội và thất bại; client chỉ gửi yêu cầu và hiển thị trạng thái.

## Luồng scene

1. Cuối Map 2, `CoopSceneTeleporter` yêu cầu đủ hai player trong trigger.
2. `SceneLoader` server-load scene `Final_Boss_Room` qua NGO.
3. `PlayerSpawner` đặt Host/Client vào `BossSpawnPoints/P1` và `BossSpawnPoints/P2`.
4. `BossEncounterManager` giữ state: `WaitingForPlayers`, `Intro`, `Active`, `WipeReset`, `Victory`.
5. Khi cả hai ở vùng vào phòng, khóa cửa và bắt đầu encounter.

## Luật hồi sinh trong boss room

- Countdown tự hồi sinh: 10 giây, hồi 100% HP tại boss respawn point.
- Revive đồng đội: player sống giữ Interact trong 5 giây ở gần player chết; hồi sinh ngay tại chỗ với 60% MaxHealth.
- Hủy revive nếu người cứu rời khoảng cách, chết, hoặc mục tiêu đã được hồi sinh.
- Nếu cả hai player cùng chết, server kích hoạt wipe ngay, hủy mọi countdown/revive và reset encounter.
- `RespawnManager` dùng cho map thường không được chạy song song với `BossRespawnPolicy` (scene boss hiện vẫn giữ component legacy; cần disable khi encounter prefab hoàn thiện).

## Thành phần runtime dự kiến

- `BossEncounterManager : NetworkBehaviour`: state machine, start/reset/wipe/victory.
- `BossRespawnPolicy`: countdown, revive validation, auto-respawn và wipe detection.
- `ReviveInteractable`: hold-interaction 5 giây, server-authoritative.
- `BossEncounterHUD`: countdown, revive progress, wipe và state presentation.
- `BossEncounterConfig : ScriptableObject`: delay, phần trăm HP, khoảng cách, spawn points và reset references.

## Setup scene cơ bản

- Giữ `GENERAL`, `PlayerSpawner`, ánh sáng chính và post-process volume.
- Loại bỏ `TeleportManager` vì đây là testing-only tool.
- Loại bỏ checkpoint thường; boss có spawn point riêng và không lưu checkpoint giữa encounter.
- Đã tạo hierarchy `BOSS ENCOUNTER` với `BossRespawnPoint`, `BossCenter`, `PlayerEntry` (trigger BoxCollider) và `BossArena`; dùng hai spawn point `P1/P2` hiện có của `PlayerSpawner` để không phá reference NetworkObject.
- Giữ terrain hiện tại tạm thời làm nền; thay bằng geometry arena khi layout boss được dựng.
- Thêm `Final_Boss_Room` vào Build Settings và `Constants.Scenes.BOSS_FINAL` trước khi test chuyển scene.

## Trình tự triển khai

1. Đã tạo config type, enum state, death event server-authoritative và các API health cần thiết.
2. Đã tạo `BossEncounterManager` và `BossRespawnPolicy`; policy xử lý intro/active, countdown, revive validation và wipe.
3. Tạo revive interaction và HUD.
4. Nối cổng cuối Map 2 với `Final_Boss_Room`.
5. Setup boss prefab, projectile, sàn/môi trường và reset targets.
6. Test Host + Client: scene load, chết đơn, revive thành công/hủy, auto-respawn, chết đồng thời, wipe/reset và victory.

## Validation checklist

- Scene có trong Build Settings, load được bằng `SceneLoader`.
- Tất cả NetworkObject cần thiết được spawn server-authoritative.
- Không có hai hệ thống cùng hồi sinh một player (cần disable `RespawnManager` legacy trước PlayMode boss test).
- Wipe không để lại coroutine, projectile, trạng thái sàn hoặc HUD của attempt trước.
- Chạy EditMode/PlayMode test và kiểm tra Unity Console không phát sinh lỗi mới.
