using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>Group discussion room: a table with seats agents can be sent to.</summary>
    public class MeetingRoom : MonoBehaviour
    {
        [SerializeField] private Transform[] seats;

        public int SeatCount => seats != null ? seats.Length : 0;
        public Transform GetSeat(int i) => seats[i];
    }
}
