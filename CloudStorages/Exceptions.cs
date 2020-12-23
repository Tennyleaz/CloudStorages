using System;

namespace CloudStorages
{
    public class PortUnavailableException : Exception
    {
        public int RequiredPort;        
    }
}