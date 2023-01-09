using Newtonsoft.Json;

namespace Unity.WartechStudio.Network
{
    public interface INetworkStruct
    {
        public string SerializeToJson()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
    
}
