using UnityEngine;
using TMPro;

public class GameTimer : MonoBehaviour
{
    public float timeRemaining = 300f;
    public TMP_Text[] timerTexts;   // 改动1：从一个变成一组（数组）

    void Update()
    {
        if (timeRemaining > 0)
        {
            timeRemaining -= Time.deltaTime;
        }
        else
        {
            timeRemaining = 0;
        }

        int minutes = Mathf.FloorToInt(timeRemaining / 60);
        int seconds = Mathf.FloorToInt(timeRemaining % 60);

        // 改动2：先把显示的文字算好，存进一个变量
        string display = string.Format("{0:00}:{1:00}", minutes, seconds);

        // 改动3：遍历数组，给每一块表都设置同样的文字
        foreach (TMP_Text t in timerTexts)
        {
            if (t != null)
            {
                t.text = display;
            }
        }
    }
}