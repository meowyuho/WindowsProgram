using AForge.Controls;
using AForge.Video;
using AForge.Video.DirectShow;
using Baidu.Aip.Face;
using BaiduAI.Common;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace BaiduAI
{
    public partial class Form1 : Form
    {
        // 百度AI平台的认证信息
        private string APP_ID = "119152692";
        private string API_KEY = "gP0E0dHFocgWacyDJSVZ1xxI";
        private string SECRET_KEY = "QUoeQ7xfFvH3Cibfzmrhg0QDfPjJYfIL";

        private Face client = null;                                     // 百度人脸识别客户端
        private bool IsStart = false;                                   // 视频帧处理启动标志
        private FaceLocation location = null;                           // 检测到的人脸位置信息
        private FilterInfoCollection videoDevices = null;               // 视频设备集合
        private VideoCaptureDevice videoSource;                         // 视频捕获设备
        private CancellationTokenSource _cancellationTokenSource;       // 异步任务取消令牌

        public Form1()
        {
            InitializeComponent();
            axWindowsMediaPlayer1.uiMode = "Invisible";     // 隐藏媒体播放器UI
            client = new Face(API_KEY, SECRET_KEY);         // 初始化人脸识别客户端
        }

        public string ConvertImageToBase64(Image file)
        {
            if (file == null) return null;
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    file.Save(ms, ImageFormat.Jpeg);
                    byte[] bytes = ms.ToArray();
                    return Convert.ToBase64String(bytes);
                }
            }
            catch (Exception ex)
            {
                ClassLoger.Error("ConvertImageToBase64", ex);
                return null;
            }
        }
        // 单张人脸检测（从文件选择）
        private void button1_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory,
                Filter = "所有文件|*.*",
                RestoreDirectory = true,
                FilterIndex = 1
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string filename = dialog.FileName;
                try
                {
                    Image im = Image.FromFile(filename);
                    string image = ConvertImageToBase64(im);
                    string imageType = "BASE64";

                    var options = new Dictionary<string, object>
                    {
                        {"face_field", "age,beauty"},
                        {"face_fields", "age,qualities,beauty"}
                    };

                    var result = client.Detect(image, imageType, options);
                    textBox1.Text = result.ToString();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        public string ReadImg(string img)
        {
            return Convert.ToBase64String(File.ReadAllBytes(img));
        }
        
        //两张人脸对比
        private void button2_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(textBox2.Text) || string.IsNullOrEmpty(textBox3.Text))
            {
                MessageBox.Show("请选择要对比的人脸图片");
                return;
            }
            try
            {
                string path1 = textBox2.Text;
                string path2 = textBox3.Text;

                var faces = new JArray
                {
                    new JObject
                    {
                        {"image", ReadImg(path1)},
                        {"image_type", "BASE64"},
                        {"face_type", "LIVE"},
                        {"quality_control", "LOW"},
                        {"liveness_control", "NONE"},
                    },
                    new JObject
                    {
                        {"image", ReadImg(path2)},
                        {"image_type", "BASE64"},
                        {"face_type", "LIVE"},
                        {"quality_control", "LOW"},
                        {"liveness_control", "NONE"},
                    }
                 };

                var result = client.Match(faces);
                textBox1.Text = result.ToString();
            }
            catch (Exception ex) { }
        }

        // 选择人脸对比图片
        private void button3_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory,
                Filter = "所有文件|*.*",
                RestoreDirectory = true,
                FilterIndex = 2
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                if (string.IsNullOrEmpty(textBox2.Text))
                    textBox2.Text = dialog.FileName;
                else
                    textBox3.Text = dialog.FileName;
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            _cancellationTokenSource = new CancellationTokenSource();

            videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (videoDevices != null && videoDevices.Count > 0)
            {
                foreach (FilterInfo device in videoDevices)
                    comboBox1.Items.Add(device.Name);
                comboBox1.SelectedIndex = 0;
            }

            videoSourcePlayer1.NewFrame += VideoSourcePlayer1_NewFrame;

            ThreadPool.QueueUserWorkItem(state => {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    IsStart = true;
                    try { Task.Delay(500, _cancellationTokenSource.Token).Wait(); }
                    catch (OperationCanceledException) { break; }
                }
            });
        }

        private void VideoSourcePlayer1_NewFrame(object sender, ref Bitmap image)
        {
            try
            {
                if (IsStart)
                {
                    IsStart = false;
                    //ThreadPool.QueueUserWorkItem(new WaitCallback(this.Detect), image.Clone());
                }

                if (location != null)
                {
                    try
                    {
                        Graphics g = Graphics.FromImage(image);
                        g.DrawLine(new Pen(Color.Black), new System.Drawing.Point(location.left, location.top), new System.Drawing.Point(location.left + location.width, location.top));
                        g.DrawLine(new Pen(Color.Black), new System.Drawing.Point(location.left, location.top), new System.Drawing.Point(location.left, location.top + location.height));
                        g.DrawLine(new Pen(Color.Black), new System.Drawing.Point(location.left, location.top + location.height), new System.Drawing.Point(location.left + location.width, location.top + location.height));
                        g.DrawLine(new Pen(Color.Black), new System.Drawing.Point(location.left + location.width, location.top), new System.Drawing.Point(location.left + location.width, location.top + location.height));
                        g.Dispose();
                    }
                    catch (Exception ex)
                    {
                        ClassLoger.Error("VideoSourcePlayer1_NewFrame", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                ClassLoger.Error("VideoSourcePlayer1_NewFrame1", ex);
            }
        }

        // 摄像头连接方法
        private void CameraConn()
        {
            if (comboBox1.Items.Count <= 0)
            {
                MessageBox.Show("请插入视频设备");
                return;
            }

            videoSource = new VideoCaptureDevice(videoDevices[comboBox1.SelectedIndex].MonikerString);
            videoSource.DesiredFrameSize = new System.Drawing.Size(320, 240);
            videoSource.DesiredFrameRate = 1;

            videoSourcePlayer1.VideoSource = videoSource;
            videoSourcePlayer1.Start();
        }

        private void button6_Click(object sender, EventArgs e)
        {
            videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (videoDevices != null && videoDevices.Count > 0)
            {
                comboBox1.Items.Clear();
                foreach (FilterInfo device in videoDevices)
                    comboBox1.Items.Add(device.Name);
                comboBox1.SelectedIndex = 0;
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            // 1. 获取当前视频帧
            // 2. 调整图片尺寸（最大800x600）
            // 3. 保存图片到本地PersonImg目录
            // 4. 调用人脸检测API
            // 5. 解析并显示年龄/美颜度
            // 6. 在界面更新人脸位置信息
            if (comboBox1.Items.Count <= 0)
            {
                MessageBox.Show("请插入视频设备");
                return;
            }
            try
            {
                if (videoSourcePlayer1.IsRunning)
                {
                    Bitmap currentFrame = videoSourcePlayer1.GetCurrentVideoFrame();
                    if (currentFrame == null)
                    {
                        MessageBox.Show("无法获取视频帧", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    Bitmap resizedFrame = null;
                    string picName = GetImagePath() + "\\" + DateTime.Now.ToFileTime() + ".jpg";

                    try
                    {
                        int maxWidth = 800, maxHeight = 600;
                        double ratioX = (double)maxWidth / currentFrame.Width;
                        double ratioY = (double)maxHeight / currentFrame.Height;
                        double ratio = Math.Min(ratioX, ratioY);

                        int newWidth = (int)(currentFrame.Width * ratio);
                        int newHeight = (int)(currentFrame.Height * ratio);

                        resizedFrame = new Bitmap(newWidth, newHeight);
                        using (Graphics g = Graphics.FromImage(resizedFrame))
                        {
                            g.DrawImage(currentFrame, 0, 0, newWidth, newHeight);
                        }

                        if (File.Exists(picName)) File.Delete(picName);
                        resizedFrame.Save(picName, ImageFormat.Jpeg);

                        try
                        {
                            Image imageForDetect = resizedFrame;
                            string imageBase64 = ConvertImageToBase64(imageForDetect);
                            string imageType = "BASE64";

                            var options = new Dictionary<string, object> { { "face_field", "age,beauty" } };
                            var result = client.Detect(imageBase64, imageType, options);

                            if (result["error_code"].Value<int>() == 0 &&
                                result["result"] != null &&
                                result["result"]["face_list"] != null &&
                                result["result"]["face_list"].Count() > 0)
                            {
                                JToken faceInfo = result["result"]["face_list"][0];
                                string age = faceInfo["age"].ToString();
                                string beauty = faceInfo["beauty"].ToString();

                                this.location = new FaceLocation
                                {
                                    left = (int)faceInfo["location"]["left"],
                                    top = (int)faceInfo["location"]["top"],
                                    width = (int)faceInfo["location"]["width"],
                                    height = (int)faceInfo["location"]["height"]
                                };

                                ageText.Text = age;
                                textBox4.Text = beauty;
                                MessageBox.Show($"图片已成功保存至：\n{picName}\n\n人脸信息：\n年龄：{age}\n美颜度：{beauty}",
                                    "拍照成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            else
                            {
                                MessageBox.Show($"图片已成功保存至：\n{picName}\n\n未检测到人脸信息",
                                    "拍照成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"图片已成功保存至：\n{picName}\n\n人脸分析异常：{ex.Message}",
                                "拍照成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            ClassLoger.Error("button5_Click", ex);
                        }
                    }
                    finally
                    {
                        if (resizedFrame != null) resizedFrame.Dispose();
                        if (currentFrame != null) currentFrame.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("拍照过程中发生错误：" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string GetImagePath()
        {
            string personImgPath = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory) + "\\PersonImg";
            if (!Directory.Exists(personImgPath)) Directory.CreateDirectory(personImgPath);
            return personImgPath;
        }

        private void button4_Click(object sender, EventArgs e)
        {
            CameraConn();
        }

        public byte[] Bitmap2Byte(Bitmap bitmap)
        {
            try
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Jpeg);
                    byte[] data = new byte[stream.Length];
                    stream.Seek(0, SeekOrigin.Begin);
                    stream.Read(data, 0, Convert.ToInt32(stream.Length));
                    return data;
                }
            }
            catch (Exception ex) { }
            return null;
        }

        public byte[] BitmapSource2Byte(BitmapSource source)
        {
            try
            {
                JpegBitmapEncoder encoder = new JpegBitmapEncoder { QualityLevel = 100 };
                using (MemoryStream stream = new MemoryStream())
                {
                    encoder.Frames.Add(BitmapFrame.Create(source));
                    encoder.Save(stream);
                    byte[] bit = stream.ToArray();
                    stream.Close();
                    return bit;
                }
            }
            catch (Exception ex)
            {
                ClassLoger.Error("BitmapSource2Byte", ex);
            }
            return null;
        }

        public void Detect(object image)
        {
            // 1. 调整图片尺寸（最大400x300）
            // 2. 转换为Base64格式
            // 3. 调用人脸检测API
            // 4. 解析返回结果：
            //    - 年龄
            //    - 人脸质量（模糊度、遮挡等）
            //    - 美颜度
            // 5. 在UI线程更新显示结果
            if (image != null && image is Bitmap)
            {
                Bitmap img = (Bitmap)image;
                Bitmap smallerImg = null;
                try
                {
                    int maxWidth = 400, maxHeight = 300;
                    double ratioX = (double)maxWidth / img.Width;
                    double ratioY = (double)maxHeight / img.Height;
                    double ratio = Math.Min(ratioX, ratioY);

                    if (ratio < 1.0)
                    {
                        int newWidth = (int)(img.Width * ratio);
                        int newHeight = (int)(img.Height * ratio);
                        smallerImg = new Bitmap(newWidth, newHeight);
                        using (Graphics g = Graphics.FromImage(smallerImg))
                        {
                            g.DrawImage(img, 0, 0, newWidth, newHeight);
                        }
                    }
                    else
                    {
                        smallerImg = new Bitmap(img);
                    }

                    var imgByte = Bitmap2Byte(smallerImg);
                    string image1 = ConvertImageToBase64(smallerImg);
                    string imageType = "BASE64";

                    if (imgByte != null)
                    {
                        var options = new Dictionary<string, object>
                        {
                            {"max_face_num", 2},
                            {"face_field", "age,qualities,beauty"}
                        };

                        var result = client.Detect(image1, imageType, options);
                        FaceDetectInfo detect = JsonHelper.DeserializeObject<FaceDetectInfo>(result.ToString());

                        this.Invoke((MethodInvoker)delegate {
                            if (detect != null && detect.result_num > 0)
                            {
                                ageText.Text = detect.result[0].age.TryToString();
                                this.location = detect.result[0].location;

                                StringBuilder sb = new StringBuilder();
                                if (detect.result[0].qualities != null)
                                {
                                    if (detect.result[0].qualities.blur >= 0.7) sb.AppendLine("人脸过于模糊");
                                    if (detect.result[0].qualities.completeness >= 0.4) sb.AppendLine("人脸不完整");
                                    if (detect.result[0].qualities.illumination <= 40) sb.AppendLine("灯光光线质量不好");

                                    if (detect.result[0].qualities.occlusion != null)
                                    {
                                        if (detect.result[0].qualities.occlusion.left_cheek >= 0.8) sb.AppendLine("左脸颊不清晰");
                                        if (detect.result[0].qualities.occlusion.left_eye >= 0.6) sb.AppendLine("左眼不清晰");
                                        if (detect.result[0].qualities.occlusion.mouth >= 0.7) sb.AppendLine("嘴巴不清晰");
                                        if (detect.result[0].qualities.occlusion.nose >= 0.7) sb.AppendLine("鼻子不清晰");
                                        if (detect.result[0].qualities.occlusion.right_cheek >= 0.8) sb.AppendLine("右脸颊不清晰");
                                        if (detect.result[0].qualities.occlusion.right_eye >= 0.6) sb.AppendLine("右眼不清晰");
                                        if (detect.result[0].qualities.occlusion.chin >= 0.6) sb.AppendLine("下巴不清晰");
                                    }
                                }

                                if (detect.result[0].location.height <= 100 || detect.result[0].location.width <= 100)
                                    sb.AppendLine("人脸部分过小");

                                textBox4.Text = sb.ToString();
                                if (textBox4.Text.IsNull()) textBox4.Text = "OK";
                            }
                        });
                    }
                }
                finally
                {
                    if (smallerImg != null) smallerImg.Dispose();
                    if (img != null && img != image) img.Dispose();
                }
            }
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            if (videoSource != null && videoSource.IsRunning) videoSource.Stop();
            Thread.Sleep(100);
            System.Environment.Exit(0);
        }

        // 人脸注册
        private void button7_Click(object sender, EventArgs e)
        {
            // 1. 获取用户ID和组ID
            // 2. 捕获当前视频帧
            // 3. 调用百度人脸注册API
            // 4. 显示注册结果
            string uid = "1";
            string userInfo = textBox6.Text.Trim();
            string groupId = textBox5.Text.Trim();

            if (comboBox1.Items.Count <= 0)
            {
                MessageBox.Show("请插入视频设备");
                return;
            }
            try
            {
                if (videoSourcePlayer1.IsRunning)
                {
                    BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        videoSourcePlayer1.GetCurrentVideoFrame().GetHbitmap(),
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    var img = BitmapSource2Byte(bitmapSource);

                    var options = new Dictionary<string, object> { { "action_type", "REPLACE" } };
                    var result = client.UserAdd(Convert.ToBase64String(img), "BASE64", groupId, uid, options);

                    if (result.Value<int>("error_code") == 0)
                        MessageBox.Show("注册成功");
                    else
                        MessageBox.Show("注册失败:" + result.ToString());
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("摄像头异常：" + ex.Message);
            }
        }

        //人脸登录
        private void button8_Click(object sender, EventArgs e)
        {
            string groupId = textBox5.Text.Trim();
            if (string.IsNullOrEmpty(groupId))
            {
                MessageBox.Show("请输入用户组ID", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (comboBox1.Items.Count <= 0)
            {
                MessageBox.Show("请插入视频设备");
                return;
            }

            try
            {
                if (videoSourcePlayer1.IsRunning)
                {
                    BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        videoSourcePlayer1.GetCurrentVideoFrame().GetHbitmap(),
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    var img = BitmapSource2Byte(bitmapSource);
                    var options = new Dictionary<string, object>
                    {
                        {"match_threshold", 70},
                        {"quality_control", "NORMAL"},
                        {"liveness_control", "LOW"},
                        {"max_user_num", 3}
                    };

                    var image = Convert.ToBase64String(img);
                    var imageType = "BASE64";
                    var result = client.Search(image, imageType, groupId, options);

                    if (result.Value<int>("error_code") == 0)
                    {
                        if (result["result"] != null &&
                            result["result"]["user_list"] != null &&
                            result["result"]["user_list"].Count() > 0)
                        {
                            JArray array = result["result"].Value<JArray>("user_list");
                            textBox7.Text = array[0].Value<string>("user_id");
                            double score = array[0].Value<double>("score");

                            axWindowsMediaPlayer1.URL = "20230522_160638_1.mp3";
                            axWindowsMediaPlayer1.Ctlcontrols.play();

                            MessageBox.Show($"登录成功！用户ID: {textBox7.Text}, 相似度: {score}",
                                "登录成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("未找到匹配的用户，请先注册或检查用户组ID是否正确",
                                "未找到用户", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    else
                    {
                        MessageBox.Show($"登录失败: {result["error_msg"]}",
                            "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("摄像头异常：" + ex.Message);
            }
        }

        private void button9_Click(object sender, EventArgs e)
        {
            axWindowsMediaPlayer1.Ctlcontrols.stop();
            if (videoDevices == null || videoDevices.Count == 0) return;
            videoSource.Stop();
            videoSourcePlayer1.Stop();
        }

        private void ageText_TextChanged(object sender, EventArgs e) { }
        private void textBox4_TextChanged(object sender, EventArgs e) { }
    }
}
