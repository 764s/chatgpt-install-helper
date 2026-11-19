using System.Runtime.InteropServices;

namespace ChatGPTInstaller
{
    // WUA 接受 DISPID 0 的完成/进度回调。工作线程通过 job.GetProgress 读取一致快照。
    [ComVisible(true), ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class UpdateJobCallback
    {
        [DispId(0)]
        public void Invoke(object job, object args) { }
    }
}
