using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
class IconTests {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr module,string name,uint type,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    static void Main(string[] args) {
        string path=Path.GetFullPath(args[0]);
        int[] sizes={16,20,24,30,32,36,40,48,60,64,72,96,128,256};
        using(var reader=new BinaryReader(File.OpenRead(path))) {
            if(reader.ReadUInt16()!=0||reader.ReadUInt16()!=1||reader.ReadUInt16()!=sizes.Length)throw new Exception("Bad icon directory");
            foreach(int size in sizes) {
                int width=reader.ReadByte(),height=reader.ReadByte();
                if((width==0?256:width)!=size||(height==0?256:height)!=size)throw new Exception("Missing exact-size frame: "+size);
                reader.ReadBytes(14);
            }
        }
        foreach(int size in sizes) {
            IntPtr handle=LoadImage(IntPtr.Zero,path,1,size,size,0x10);
            if(handle==IntPtr.Zero)throw new Exception("Windows cannot load frame: "+size);
            try {
                using(var icon=Icon.FromHandle(handle)) using(var bitmap=icon.ToBitmap()) {
                    if(bitmap.Width!=size||bitmap.Height!=size)throw new Exception("Wrong native dimensions");
                    int black=0;
                    // Inside the logo: only opaque lime or pure black, never a resampled gray/green fringe.
                    for(int y=(int)Math.Round(size*10.0/48);y<(int)Math.Round(size*39.0/48);y++)
                        for(int x=(int)Math.Round(size*13.0/48);x<(int)Math.Round(size*37.0/48);x++) {
                            var pixel=bitmap.GetPixel(x,y);
                            if(pixel.ToArgb()==Color.Black.ToArgb())black++;
                            else if(pixel.ToArgb()!=Color.FromArgb(189,255,98).ToArgb())throw new Exception("Blurred L edge at "+size+" px: "+x+","+y);
                        }
                    if(black==0)throw new Exception("L missing");
                }
            } finally { DestroyIcon(handle); }
        }
        Console.WriteLine("PASS: 14 exact native sizes, opaque black L edges, correct green and Windows icon decoding.");
    }
}
