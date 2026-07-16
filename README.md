# The Flower

![Unity Version](https://img.shields.io/badge/Unity-6000.0.69f1-blue.svg?style=flat-square&logo=unity)
![Networking](https://img.shields.io/badge/Networking-NGO-green.svg?style=flat-square)
![Project Status](https://img.shields.io/badge/Status-Work%20In%20Progress-orange.svg?style=flat-square)

**The Flower** là dự án tốt nghiệp của **Team Doro**, một trò chơi hành động - phiêu lưu cộng tác (cooperative action-adventure) đỉnh cao được xây dựng trên nền tảng Unity 6.

## 📖 Cốt truyện

Tại ngôi làng bình yên dưới chân núi, có một truyền thuyết về **Bông hoa Đồng Tâm** — một loài hoa quý hiếm mang sức mạnh chữa lành và gắn kết, chỉ nở rộ trên đỉnh **Đỉnh núi Mây Ấm** quanh năm sương mù bao phủ.

Người chơi sẽ hóa thân vào hai anh em trong một gia đình nghèo. Để tìm món quà ý nghĩa nhất dành tặng cho người mẹ kính yêu, hai anh em đã quyết định dấn thân vào một cuộc hành trình đầy gian nan nhưng cũng đầy tình cảm. Cuộc hành trình bắt đầu từ ngôi làng quen thuộc, băng qua những sa mạc cát cháy, len lỏi qua những khu rừng già rậm rạp và cuối cùng là chinh phục đỉnh núi cao vợi để tìm thấy "Đồng Tâm".

## 🎮 Lối chơi

*   **Hợp tác 2 người (2-Player Co-op):** Trải nghiệm hành trình cùng bạn bè thông qua hệ thống mạng. Mọi thử thách đều đòi hỏi sự phối hợp nhịp nhàng giữa hai người chơi.
*   **Hệ thống chiến đấu & Kỹ năng:** Cơ chế tấn công combo mượt mà cùng các kỹ năng đặc biệt của từng nhân vật.
*   **Tương tác môi trường (Co-op Interactions):** Các cơ quan, cánh cửa và câu đố chỉ có thể vượt qua khi cả hai người chơi cùng đồng tâm hiệp lực.
*   **Môi trường đa dạng:** Khám phá các vùng đất với địa hình và kẻ thù đặc trưng:
    *   **Ngôi làng:** Điểm xuất phát yên bình.
    *   **Sa mạc:** Thử thách sự kiên trì.
    *   **Rừng già:** Đối đầu với những sinh vật kỳ bí.
    *   **Đỉnh núi Mây Ấm:** Trận chiến cuối cùng và đích đến của tình anh em.

## 🎮 Hệ thống điều khiển

*   **Hệ thống điều khiển đa năng:** 
    *   Hỗ trợ hoàn hảo cho cả **Bàn phím (Keyboard)** và **Tay cầm (Gamepad)**.
    *   **Giao diện động (Dynamic UI):** Tự động thay đổi bộ icon và chỉ dẫn (prompts) tương ứng với thiết bị người chơi đang sử dụng, đảm bảo trải nghiệm mượt mà nhất.
*   **Cơ chế chiến đấu chuyên sâu:** Hệ thống combo và kỹ năng nhân vật được tối ưu hóa cho phản hồi lực (feedback) cực tốt.

## 🛠️ Công nghệ sử dụng

Dự án tận dụng những tính năng mới nhất và mạnh mẽ nhất của **Unity 6**:

*   **Networking:**
    *   **Netcode for GameObjects (NGO):** Khung kết nối chính cho trải nghiệm multiplayer.
    *   **Unity Services (UGS):** Tích hợp **Authentication** (Xác thực) và **Relay** (Kết nối ngang hàng không qua port-forwarding).
*   **Artificial Intelligence (AI):**
    *   Sử dụng gói **Unity Behavior** mới để xây dựng AI kẻ địch thông qua State Machine và Behavior Trees trực quan.
    *   **AI Navigation:** Hệ thống tìm đường thông minh cho các sinh vật trong thế giới.
*   **Rendering:** **Universal Render Pipeline (URP)** đảm bảo hiệu năng tối ưu trên nhiều nền tảng với hình ảnh hiện đại.
*   **Input System:** Xử lý điều khiển đa thiết bị (Gamepad, Keyboard) linh hoạt.
*   **Core Systems:** Kiến trúc hướng sự kiện (Event-driven) thông qua `EventBus` giúp hệ thống hoạt động ổn định và dễ dàng mở rộng.

## 📅 Thông tin dự án

*   **Team thực hiện:** Team Doro
*   **Ngày khởi công:** 12/03/2026
*   **Tình trạng:** Đang phát triển

---

## Generated asset manifest

| Name | Description | In-game Size | Path | Cost |
|------|-------------|--------------|------|------|
| Monkey sticker sets | 16 player emote stickers across two selectable sets | 180x180 px world-space HUD | `Assets/_Game/GeneratedAssets/Resources/Stickers/` | $0 (user source + ChatGPT image generation) |

© 2026 Team Doro. All rights reserved.
