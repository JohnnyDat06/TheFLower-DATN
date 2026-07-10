using UnityEngine;
using Unity.Netcode;

namespace Game.Environment
{
    /// <summary>
    /// AirLiftTrigger — Xử lý việc đẩy nhân vật bay lên cao khi bước vào vùng Trigger (ví dụ: cột khói, gió thổi).
    /// Tối ưu hóa cho Host/Client bằng cách chỉ chạy logic trên Owner của nhân vật.
    /// </summary>
    public class AirLiftTrigger : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float _liftForce = 15f;
        [SerializeField] private float _centeringForce = 5f; // Lực hút nhân vật vào giữa cột khói
        [SerializeField] private bool _isContinuous = true;

        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent<PlayerController>(out var playerController) && playerController.IsOwner)
            {
                // Chuyển trạng thái sang Jump để nhân vật có thể điều hướng trên không
                // Dùng một lực Bounce cực nhỏ để kích hoạt logic State mà không làm thay đổi hướng đẩy chính
                playerController.Bounce(0.1f); 
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (!_isContinuous) return;

            if (other.TryGetComponent<PlayerController>(out var playerController) && playerController.IsOwner)
            {
                if (other.TryGetComponent<Rigidbody>(out var rb))
                {
                    // 1. Đẩy dọc theo hướng của cột khói (trục Y của Collider)
                    Vector3 liftDir = transform.up; 
                    rb.AddForce(liftDir * _liftForce, ForceMode.Acceleration);

                    // 2. Lực hút vào tâm cột khói để giữ nhân vật không bị bay ra ngoài
                    // Tính toán vị trí của Player so với trục giữa của Collider
                    Vector3 localPos = transform.InverseTransformPoint(other.transform.position);
                    
                    // Loại bỏ thành phần Y (độ cao dọc theo cột) để chỉ lấy độ lệch ngang so với tâm
                    Vector3 localCenterOffset = new Vector3(localPos.x, 0, localPos.z);
                    
                    // Chuyển hướng lệch này về không gian thế giới và đảo ngược nó để tạo lực hút vào
                    Vector3 worldCenteringDir = transform.TransformDirection(-localCenterOffset).normalized;

                    // Chỉ áp dụng lực hút nếu nhân vật đang lệch khỏi tâm
                    if (localCenterOffset.magnitude > 0.1f)
                    {
                        rb.AddForce(worldCenteringDir * _centeringForce, ForceMode.Acceleration);
                    }
                }
            }
        }
    }
}
