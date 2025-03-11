using UnityEngine;
using UnityEngine.UI;

namespace StormBreakers
{
    public class IndicateSpeed : MonoBehaviour
    {
        public Rigidbody rb;
        public Text text;


        // Update is called once per frame
        void Update()
        {
            var speed = Mathf.Round(rb.linearVelocity.magnitude*3.6f);
            text.text = "SPEED = " + speed + "KM/H";
            text.color = Color.Lerp(Color.black, Color.red, Mathf.Clamp01(speed*0.02f-1f));
        }
    }
}
