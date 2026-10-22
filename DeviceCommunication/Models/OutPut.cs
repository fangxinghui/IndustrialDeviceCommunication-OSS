using System;
using Newtonsoft.Json;

namespace DeviceCommunication.Models
{
    public class OutPut:Sensor
    {
        [JsonIgnore] public Action<ControllerTag, object> OnSetOutPut;
        public virtual void SetOutPutOpen()
        {
            if(ControllerTag is null) return;
            OnSetOutPut?.Invoke(ControllerTag, true);
        }
        public virtual void SetOutPutClose()
        {
            if(ControllerTag is null)return;
            OnSetOutPut?.Invoke(ControllerTag, false);
        }
    }
}