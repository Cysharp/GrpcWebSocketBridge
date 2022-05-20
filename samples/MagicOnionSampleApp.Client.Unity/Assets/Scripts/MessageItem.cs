using System;
using UnityEngine;
using UnityEngine.UI;

namespace MagicOnionSampleApp.Client.Unity
{
    public class MessageItem : MonoBehaviour
    {
        public Text LabelReceivedAt;
        public Text LabelNickname;
        public Text LabelMessage;

        public void Initialize((Guid MessageId, DateTimeOffset ReceivedAt, string Nickname, string Message) message)
        {
            LabelReceivedAt.text = message.ReceivedAt.ToString("t");
            LabelNickname.text = message.Nickname;
            LabelMessage.text = message.Message;
        }
    }
}
