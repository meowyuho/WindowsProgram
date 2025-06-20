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
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace BaiduAI
{
    public partial class Form1 : Form
    {
        /*
         * 百度AI平台认证凭据配置区域
         * 应用身份验证核心参数
         * 需在百度AI开放平台创建应用后获取
         * 重要提示：实际部署时应避免硬编码，建议从配置文件读取
         */
        private string APP_ID = "119152692";  // 百度AI开放平台分配的应用唯一标识
        private string API_KEY = "gP0E0dHFocgWacyDJSVZ1xxI";  // 用于API调用的身份验证密钥
        private string SECRET_KEY = "QUoeQ7xfFvH3Cibfzmrhg0QDfPjJYfIL";  // 用于生成签名的安全密钥，确保请求合法性

        /*
         * 百度AI SDK核心客户端实例
         * 人脸识别功能调用入口
         * 通过构造函数传入API_KEY和SECRET_KEY完成OAuth2.0认证
         * 自动管理访问令牌的获取与刷新机制
         */
        private Face client = null;  // 百度人脸识别SDK客户端，所有API调用的核心入口

        // 构造函数初始化区域
        // 完成UI组件初始化和百度AI客户端初始化
        // 采用依赖注入设计模式可提高可测试性，但此处为简化直接初始化
        /// <summary>
        /// 人脸检测状态控制标志
        /// true表示当前可进行人脸检测操作
        /// 用于控制检测频率，避免超出API调用限制
        /// </summary>
        private bool IsStart = false;
        /// <summary>
        /// 人脸位置信息存储结构
        /// 包含检测到的人脸在图像中的坐标和尺寸
        /// 用于在视频帧上绘制人脸框
        /// </summary>
        private FaceLocation location = null;

        /* 
         * 视频设备操作相关属性
         * 基于AForge.NET框架实现摄像头控制
         * 支持多摄像头设备枚举和选择
         */
        private FilterInfoCollection videoDevices = null;  // 系统中所有可用视频输入设备的集合
        private VideoCaptureDevice videoSource;  // 当前选中的视频捕获设备实例

        private CancellationTokenSource _cancellationTokenSource;  // 用于取消异步操作的令牌源

        /*
         * 窗体构造函数
         * 执行初始化操作：
         * 1. 调用设计器生成的初始化代码
         * 2. 配置Windows Media Player为无界面模式（仅用于音频播放）
         * 3. 初始化百度AI人脸识别客户端
         */
        public Form1()
        {
            InitializeComponent();
            // 配置媒体播放器为无界面模式，仅用于播放提示音
            axWindowsMediaPlayer1.uiMode = "Invisible";
            // 初始化百度AI客户端，传入API_KEY和SECRET_KEY完成认证
            client = new Face(API_KEY, SECRET_KEY);
        }

        /// <summary>
        /// 图像转Base64编码核心方法
        /// 将Image对象转换为Base64字符串，满足百度AI接口要求
        /// 实现要点：
        /// 1. 使用MemoryStream进行内存操作
        /// 2. 显式指定JPEG格式避免RawFormat可能的兼容性问题
        /// 3. 包含完整的异常处理和日志记录
        /// </summary>
        /// <param name="file">待转换的Image对象，不可为null</param>
        /// <returns>Base64编码的字符串，转换失败时返回null</returns>
        /// <remarks>
        /// 百度AI接口要求图像数据必须以Base64格式传输
        /// 此方法是图像数据与API之间的桥梁
        /// 注意：JPEG格式会有一定程度的压缩，可能影响识别精度
        /// </remarks>
        public string ConvertImageToBase64(Image file)
        {
            if (file == null)
            {
                return null;
            }

            try
            {
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    // 显式指定JPEG格式，解决RawFormat可能导致的编码器为null问题
                    file.Save(memoryStream, ImageFormat.Jpeg);
                    byte[] imageBytes = memoryStream.ToArray();  // 获取图像字节数组
                    return Convert.ToBase64String(imageBytes);  // 转换为Base64字符串
                }
            }
            catch (Exception ex)
            {
                // 记录详细的错误日志，包括异常类型和堆栈信息
                ClassLoger.Error("ConvertImageToBase64", ex);
                return null;
            }
        }

        /// <summary>
        /// 本地图片人脸检测按钮事件处理
        /// 实现从本地选择图片并进行人脸检测的完整流程：
        /// 1. 打开文件选择对话框
        /// 2. 加载选中图片并转换为Base64
        /// 3. 配置百度AI检测参数
        /// 4. 调用检测API并显示结果
        /// </summary>
        /// <param name="sender">事件触发源对象</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// 此功能用于非实时场景下的人脸检测
        /// 支持配置多种检测参数，如：
        /// - max_face_num：最多检测人脸数量
        /// - face_field：需要返回的人脸属性
        /// 注意：代码中提供了两组参数配置示例，可根据需求选择
        /// </remarks>
        private void button1_Click(object sender, EventArgs e)
        {
            // 初始化文件选择对话框
            OpenFileDialog dialog = new OpenFileDialog();
            // 设置初始目录为应用程序所在目录
            dialog.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
            // 设置文件筛选器，显示所有类型文件
            dialog.Filter = "所有文件|*.*";
            dialog.RestoreDirectory = true;
            dialog.FilterIndex = 1;

            // 显示文件选择对话框，用户确认选择后执行后续操作
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string filename = dialog.FileName;
                try
                {
                    // 加载选中的图片文件
                    Image im = Image.FromFile(filename);
                    // 转换为Base64格式
                    var image = ConvertImageToBase64(im);
                    string imageType = "BASE64";  // 指定图像编码类型为BASE64

                    /* 
                     * 百度人脸检测API参数配置方案一
                     * 包含参数说明：
                     * face_field：指定返回的人脸属性，age(年龄)和beauty(颜值)
                     * face_fields：指定返回的人脸特征字段，包含年龄、质量和颜值
                     * 注意：face_field和face_fields参数存在功能重叠，实际使用时需注意
                     */
                    var options = new Dictionary<string, object>{
                        //{"max_face_num", 2},  // 最多检测2张人脸，此处注释为默认检测1张
                        {"face_field", "age,beauty"},  // 返回年龄和颜值信息
                        {"face_fields", "age,qualities,beauty"}  // 返回年龄、质量和颜值信息
                    };

                    /* 
                     * 百度人脸检测API参数配置方案二（备选）
                     * 包含更多检测控制参数：
                     * face_field：仅返回年龄信息
                     * max_face_num：最多检测2张人脸
                     * face_type：指定检测生活照中的人脸(LIVE)
                     * liveness_control：设置低级活体检测控制
                     */
                    var options1 = new Dictionary<string, object>{
                        {"face_field", "age"},
                        {"max_face_num", 2},
                        {"face_type", "LIVE"},
                        {"liveness_control", "LOW"}
                    };

                    // 调用百度AI人脸检测API，传入图像数据和参数
                    var result = client.Detect(image, imageType, options);

                    // 将API返回的JSON结果显示在文本框中
                    textBox1.Text = result.ToString();

                    // 注释掉的代码：将JSON结果反序列化为自定义对象，需配合对应的实体类
                    //FaceDetectInfo detect = JsonHelper.DeserializeObject<FaceDetectInfo>(result.ToString());

                }
                catch (Exception ex)
                {
                    // 显示友好的异常提示信息
                    MessageBox.Show(ex.Message);
                }
            }
        }

        /// <summary>
        /// 从文件路径读取图片并转换为Base64字符串
        /// 简化版图像转换方法，直接读取文件字节并编码
        /// </summary>
        /// <param name="img">图片文件完整路径</param>
        /// <returns>Base64编码的字符串，读取失败时返回null</returns>
        /// <remarks>
        /// 此方法适用于已知文件路径的场景
        /// 相比ConvertImageToBase64方法，缺少图像格式转换和异常处理
        /// 建议在对稳定性要求不高的场景使用
        /// </remarks>
        public string ReadImg(string img)
        {
            return Convert.ToBase64String(File.ReadAllBytes(img));  // 直接读取文件字节并转为Base64
        }

        /// <summary>
        /// 人脸比对功能按钮事件处理
        /// 实现两张人脸图片的相似度比对：
        /// 1. 检查是否已选择两张待比对图片
        /// 2. 构建符合API要求的JSON请求结构
        /// 3. 调用百度AI人脸比对API
        /// 4. 显示比对结果（相似度分数等）
        /// </summary>
        /// <param name="sender">事件触发源对象</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// 人脸比对是人脸识别的核心功能之一
        /// 此实现中：
        /// - 支持设置质量控制和活体检测参数
        /// - 使用JArray构建符合API要求的多人脸参数结构
        /// - 注意：实际应用中应添加图片质量校验逻辑
        /// </remarks>
        private void button2_Click(object sender, EventArgs e)
        {
            // 检查是否已选择两张待比对的图片
            if (string.IsNullOrEmpty(textBox2.Text) || string.IsNullOrEmpty(textBox3.Text))
            {
                MessageBox.Show("请选择要对比的人脸图片");
                return;
            }
            try
            {
                string path1 = textBox2.Text;  // 第一张人脸图片路径
                string path2 = textBox3.Text;  // 第二张人脸图片路径

                /* 
                 * 构建人脸比对请求参数JSON结构
                 * 使用JArray存储两张人脸的信息
                 * 每个JObject包含：
                 * - image：Base64编码的图像数据
                 * - image_type：图像编码类型
                 * - face_type：人脸类型（生活照）
                 * - quality_control：质量控制级别
                 * - liveness_control：活体检测级别
                 */
                var faces = new JArray
                {
                    new JObject
                    {
                        {"image", ReadImg(path1)},  // 第一张图片的Base64编码
                        {"image_type", "BASE64"},   // 图片编码类型
                        {"face_type", "LIVE"},      // 人脸类型：生活照
                        {"quality_control", "LOW"}, // 质量控制：低级别
                        {"liveness_control", "NONE"}, // 活体检测：不做检测
                    },
                    new JObject
                    {
                        {"image", ReadImg(path2)},  // 第二张图片的Base64编码
                        {"image_type", "BASE64"},   // 图片编码类型
                        {"face_type", "LIVE"},      // 人脸类型：生活照
                        {"quality_control", "LOW"}, // 质量控制：低级别
                        {"liveness_control", "NONE"}, // 活体检测：不做检测
                    }
                 };

                // 调用百度AI人脸比对API，传入包含两张人脸信息的JArray
                var result = client.Match(faces);
                // 显示比对结果，包含相似度分数和其他信息
                textBox1.Text = result.ToString();
            }
            catch (Exception ex)
            {
                // 异常处理，实际应用中应记录详细日志
            }
        }

        /// <summary>
        /// 选择人脸比对图片按钮事件处理
        /// 打开文件选择对话框并填充图片路径
        /// 支持连续选择两张图片（首次选择填充第一个文本框，再次选择填充第二个）
        /// </summary>
        /// <param name="sender">事件触发源对象</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// 此功能简化了用户选择多张图片的操作流程
        /// 实现逻辑：
        /// 1. 检查第一个文本框是否已填充
        /// 2. 根据检查结果决定填充哪个文本框
        /// 注意：未包含图片格式校验，实际应用中应添加
        /// </remarks>
        private void button3_Click(object sender, EventArgs e)
        {
            // 初始化文件选择对话框
            OpenFileDialog dialog = new OpenFileDialog();
            // 设置初始目录为应用程序所在目录
            dialog.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
            // 设置文件筛选器，显示所有类型文件
            dialog.Filter = "所有文件|*.*";
            dialog.RestoreDirectory = true;
            dialog.FilterIndex = 2;

            // 显示文件选择对话框，用户确认选择后执行后续操作
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                // 根据第一个文本框是否为空，决定填充哪个文本框
                if (string.IsNullOrEmpty(textBox2.Text))
                {
                    textBox2.Text = dialog.FileName;  // 填充第一个图片路径
                }
                else
                {
                    textBox3.Text = dialog.FileName;  // 填充第二个图片路径
                }
            }
        }

        /// <summary>
        /// 窗体加载事件处理
        /// 执行初始化操作：
        /// 1. 初始化取消令牌源
        /// 2. 枚举系统视频设备并添加到下拉列表
        /// 3. 注册视频帧捕获事件处理器
        /// 4. 启动人脸检测定时任务
        /// </summary>
        /// <param name="sender">事件触发源对象</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// 此方法是窗体初始化的核心
        /// 注意事项：
        /// - 视频设备枚举可能需要一定时间
        /// - 定时任务使用线程池实现，避免UI阻塞
        /// - 百度AI接口有调用频率限制（建议不超过2次/秒）
        /// </remarks>
        private void Form1_Load(object sender, EventArgs e)
        {
            // 初始化取消令牌源，用于优雅地取消异步操作
            _cancellationTokenSource = new CancellationTokenSource();

            /* 
             * 视频设备初始化流程
             * 1. 枚举系统中所有视频输入设备
             * 2. 将设备名称添加到下拉列表
             * 3. 默认选择第一个设备
             */
            videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (videoDevices != null && videoDevices.Count > 0)
            {
                foreach (FilterInfo device in videoDevices)
                {
                    comboBox1.Items.Add(device.Name);  // 添加设备名称到下拉列表
                }
                comboBox1.SelectedIndex = 0;  // 默认选择第一个设备
            }

            // 注册视频帧捕获事件处理器，新帧到达时触发VideoSourcePlayer1_NewFrame方法
            videoSourcePlayer1.NewFrame += VideoSourcePlayer1_NewFrame;

            /* 
             * 启动人脸检测定时任务
             * 使用线程池实现定时检测：
             * 1. 创建后台任务循环
             * 2. 每隔500ms设置检测标志为true
             * 3. 支持通过取消令牌优雅退出
             */
            ThreadPool.QueueUserWorkItem(state => {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    IsStart = true;
                    try
                    {
                        // 使用可取消的等待，支持任务取消
                        Task.Delay(500, _cancellationTokenSource.Token).Wait();
                    }
                    catch (OperationCanceledException)
                    {
                        break; // 收到取消请求后优雅退出
                    }
                }
            });
        }

        /// <summary>
        /// 视频帧捕获事件处理方法
        /// 处理摄像头新捕获的视频帧：
        /// 1. 根据检测标志决定是否进行人脸检测
        /// 2. 如检测到人脸，在视频帧上绘制人脸框
        /// </summary>
        /// <param name="sender">事件触发源对象</param>
        /// <param name="image">捕获的视频帧Bitmap对象</param>
        /// <remarks>
        /// 此方法在AForge视频播放控件获取新帧时触发
        /// 实现要点：
        /// - 使用线程池异步处理检测，避免UI阻塞
        /// - 人脸框绘制使用GDI+图形库
        /// - 包含完整的异常处理和资源释放
        /// </remarks>
        private void VideoSourcePlayer1_NewFrame(object sender, ref Bitmap image)
        {
            try
            {
                if (IsStart)
                {
                    IsStart = false;  // 重置检测标志，避免连续检测
                    /* 
                     * 异步人脸检测处理
                     * 使用线程池执行检测逻辑，避免UI线程阻塞
                     * 传入图像克隆副本，防止多线程访问冲突
                     * 注意：此处代码被注释，实际功能在Detect方法中实现
                     */
                    //ThreadPool.QueueUserWorkItem(new WaitCallback(this.Detect), image.Clone());
                }

                // 如果检测到人脸位置信息，在视频帧上绘制人脸框
                if (location != null)
                {
                    try
                    {
                        // 获取Graphics对象用于绘制
                        Graphics g = Graphics.FromImage(image);
                        // 绘制四条线构成人脸框（上、左、下、右边界）
                        g.DrawLine(new Pen(Color.Black), new System.Drawing.Point(location.left, location.top), new System.Drawing.Point(location.left + location.width, location.top));
                        g.DrawLine(new Pen(Color.Black), new System.Drawing.Point(location.left, location.top), new System.Drawing.Point(location.left, location.top + location.height));
                        g.DrawLine(new Pen(Color.Black), new System.Drawing.Point(location.left, location.top + location.height), new System.Drawing.Point(location.left + location.width, location.top + location.height));
                        g.DrawLine(new Pen(Color.Black), new System.Drawing.Point(location.left + location.width, location.top), new System.Drawing.Point(location.left + location.width, location.top + location.height));
                        g.Dispose();  // 释放Graphics资源，避免内存泄漏
                    }
                    catch (Exception ex)
                    {
                        // 记录绘制异常，包含异常详情
                        ClassLoger.Error("VideoSourcePlayer1_NewFrame", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                // 记录事件处理过程中的异常
                ClassLoger.Error("VideoSourcePlayer1_NewFrame1", ex);
            }
        }

        /// <summary>
        /// 连接并打开摄像头核心方法
        /// 实现摄像头初始化和视频流启动：
        /// 1. 检查是否有可用视频设备
        /// 2. 创建VideoCaptureDevice实例
        /// 3. 设置视频参数（分辨率、帧率）
        /// 4. 启动视频流
        /// </summary>
        /// <remarks>
        /// 此方法是摄像头操作的核心
        /// 注意事项：
        /// - 视频参数设置会影响性能和识别效果
        /// - 低分辨率和帧率可降低带宽和计算资源消耗
        /// - 实际应用中应添加设备打开失败处理逻辑
        /// </remarks>
        private void CameraConn()
        {
            // 检查是否有可用的视频设备
            if (comboBox1.Items.Count <= 0)
            {
                MessageBox.Show("请插入视频设备");
                return;
            }

            // 创建视频捕获设备对象，使用选中的设备
            videoSource = new VideoCaptureDevice(videoDevices[comboBox1.SelectedIndex].MonikerString);
            // 设置视频分辨率为320x240（降低带宽和计算资源消耗）
            videoSource.DesiredFrameSize = new System.Drawing.Size(320, 240);
            // 设置帧率为1帧/秒（根据百度API限制调整）
            videoSource.DesiredFrameRate = 1;

            // 将视频源设置到视频播放控件
            videoSourcePlayer1.VideoSource = videoSource;
            // 启动视频流，开始捕获图像
            videoSourcePlayer1.Start();
        }

        /// <summary>
        /// 重新检测视频设备按钮事件处理
        /// 重新枚举系统中的视频设备并更新下拉列表
        /// </summary>
        /// <param name="sender">事件触发源对象</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// 此功能用于动态添加新插入的视频设备
        /// 实现逻辑：
        /// 1. 重新获取系统视频设备列表
        /// 2. 清空并重新填充下拉列表
        /// 3. 默认选择第一个设备
        /// </remarks>
        private void button6_Click(object sender, EventArgs e)
        {
            // 重新获取系统中的视频设备列表
            videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (videoDevices != null && videoDevices.Count > 0)
            {
                // 清空现有列表项
                comboBox1.Items.Clear();
                foreach (FilterInfo device in videoDevices)
                {
                    comboBox1.Items.Add(device.Name);  // 添加设备名称到下拉列表
                }
                comboBox1.SelectedIndex = 0;  // 选择第一个设备
            }
        }

        /// <summary>
        /// 拍照按钮事件处理
        /// 实现拍照并进行人脸检测的完整流程：
        /// 1. 检查视频设备状态
        /// 2. 获取当前视频帧并处理
        /// 3. 保存图片到本地
        /// 4. 调用百度AI进行人脸检测
        /// 5. 显示检测结果
        /// </summary>
        /// <param name="sender">事件触发源对象</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// 此功能是摄像头拍照与人脸检测的结合
        /// 实现要点：
        /// - 图像尺寸调整以减少内存消耗
        /// - 完整的文件操作和异常处理
        /// - 检测结果与图片保存关联显示
        /// </remarks>
        private void button5_Click(object sender, EventArgs e)
        {
            // 检查是否有可用的视频设备
            if (comboBox1.Items.Count <= 0)
            {
                MessageBox.Show("请插入视频设备");
                return;
            }
            try
            {
                // 检查视频是否在运行
                if (videoSourcePlayer1.IsRunning)
                {
                    // 获取当前视频帧
                    Bitmap currentFrame = videoSourcePlayer1.GetCurrentVideoFrame();
                    if (currentFrame == null)
                    {
                        MessageBox.Show("无法获取视频帧", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    // 创建较小尺寸的Bitmap来减少内存消耗
                    Bitmap resizedFrame = null;
                    // 生成唯一的图片文件名（使用时间戳）
                    string picName = GetImagePath() + "\\" + DateTime.Now.ToFileTime() + ".jpg";

                    try
                    {
                        // 图像尺寸调整逻辑，保持宽高比并限制最大尺寸
                        int maxWidth = 800;
                        int maxHeight = 600;

                        // 计算新尺寸，保持宽高比
                        double ratioX = (double)maxWidth / currentFrame.Width;
                        double ratioY = (double)maxHeight / currentFrame.Height;
                        double ratio = Math.Min(ratioX, ratioY);

                        int newWidth = (int)(currentFrame.Width * ratio);
                        int newHeight = (int)(currentFrame.Height * ratio);

                        // 创建缩小的图像
                        resizedFrame = new Bitmap(newWidth, newHeight);
                        using (Graphics g = Graphics.FromImage(resizedFrame))
                        {
                            g.DrawImage(currentFrame, 0, 0, newWidth, newHeight);
                        }

                        // 检查文件是否已存在，存在则删除
                        if (File.Exists(picName))
                        {
                            File.Delete(picName);
                        }

                        // 保存为JPEG格式图片
                        resizedFrame.Save(picName, ImageFormat.Jpeg);

                        try
                        {
                            // 获取处理后的图像用于检测
                            Image imageForDetect = resizedFrame;
                            // 输出调试信息，验证图像有效性
                            Console.WriteLine($"图像格式: {imageForDetect.RawFormat}, 尺寸: {imageForDetect.Width}x{imageForDetect.Height}");

                            // 转换为Base64格式
                            var imageBase64 = ConvertImageToBase64(imageForDetect);
                            // 输出调试信息，验证Base64编码结果
                            Console.WriteLine($"Base64编码长度: {imageBase64?.Length ?? 0}");

                            string imageType = "BASE64";

                            // 记录API参数配置，便于调试
                            var options = new Dictionary<string, object>{
                                {"face_field", "age,beauty"}
                            };
                            Console.WriteLine($"API参数: {string.Join(", ", options.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");

                            // 调用百度AI人脸检测API
                            var result = client.Detect(imageBase64, imageType, options);

                            // 输出API返回结果，便于调试
                            Console.WriteLine("API返回结果: " + result.ToString());

                            // 解析检测结果，处理成功场景
                            if (result["error_code"].Value<int>() == 0 &&
                                result["result"] != null &&
                                result["result"]["face_list"] != null &&
                                result["result"]["face_list"].Count() > 0)
                            {
                                // 获取第一张人脸的信息
                                JToken faceInfo = result["result"]["face_list"][0];
                                string age = faceInfo["age"].ToString();
                                string beauty = faceInfo["beauty"].ToString();

                                // 保存人脸位置信息用于显示
                                this.location = new FaceLocation
                                {
                                    left = (int)faceInfo["location"]["left"],
                                    top = (int)faceInfo["location"]["top"],
                                    width = (int)faceInfo["location"]["width"],
                                    height = (int)faceInfo["location"]["height"]
                                };

                                // 显示年龄和颜值信息
                                ageText.Text = age;
                                textBox4.Text = beauty;
                                // 显示包含保存路径和人脸信息的综合提示
                                MessageBox.Show($"图片已成功保存至：\n{picName}\n\n人脸信息：\n年龄：{age}\n美颜度：{beauty}",
                                    "拍照成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            else
                            {
                                // 未检测到人脸的场景处理
                                MessageBox.Show($"图片已成功保存至：\n{picName}\n\n未检测到人脸信息",
                                    "拍照成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                        }
                        catch (Exception ex)
                        {
                            // 记录详细的异常信息，包括类型、消息和堆栈
                            Console.WriteLine($"异常类型: {ex.GetType().FullName}");
                            Console.WriteLine($"异常消息: {ex.Message}");
                            Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");

                            // 显示友好的错误提示
                            MessageBox.Show($"图片已成功保存至：\n{picName}\n\n人脸分析异常：{ex.Message}",
                                "拍照成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                            // 记录错误到日志系统
                            ClassLoger.Error("button5_Click", ex);
                        }
                    }
                    finally
                    {
                        // 确保释放图像资源，避免内存泄漏
                        if (resizedFrame != null)
                        {
                            resizedFrame.Dispose();
                        }
                        if (currentFrame != null)
                        {
                            currentFrame.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 显示拍照过程中的异常信息
                MessageBox.Show("拍照过程中发生错误：" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 获取图片保存路径方法
        /// 确保PersonImg目录存在，不存在则创建
        /// </summary>
        /// <returns>图片保存目录的完整路径</returns>
        /// <remarks>
        /// 此方法实现了图片文件的规范存储
        /// 实现逻辑：
        /// 1. 构建PersonImg目录路径（应用程序目录下）
        /// 2. 检查目录是否存在，不存在则创建
        /// 3. 返回目录路径
        /// </remarks>
        private string GetImagePath()
        {
            // 构建PersonImg目录路径（应用程序所在目录下）
            string personImgPath = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory)
                         + Path.DirectorySeparatorChar.ToString() + "PersonImg";

            // 如果目录不存在，创建它
            if (!Directory.Exists(personImgPath))
            {
                Directory.CreateDirectory(personImgPath);
            }

            return personImgPath;
        }

        /// <summary>
        /// 启动摄像头按钮事件处理
        /// 调用CameraConn方法连接并打开摄像头
        /// </summary>
        /// <param name="sender">事件触发源对象</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// 此按钮是摄像头操作的入口
        /// 点击后执行完整的摄像头初始化流程
        /// 注意：应添加设备忙状态检测，避免重复打开
        /// </remarks>
        private void button4_Click(object sender, EventArgs e)
        {
            CameraConn();  // 调用连接摄像头的核心方法
        }

        /// <summary>
        /// Bitmap转byte[]数组工具方法
        /// 将Bitmap图像转换为JPEG格式的字节数组
        /// </summary>
        /// <param name="bitmap">待转换的Bitmap对象</param>
        /// <returns>JPEG格式的字节数组，转换失败时返回null</returns>
        /// <remarks>
        /// 此方法是图像数据处理的基础工具
        /// 实现要点：
        /// - 使用MemoryStream进行内存操作
        /// - 显式指定JPEG格式
        /// - 包含完整的异常处理
        /// </remarks>
        public byte[] Bitmap2Byte(Bitmap bitmap)
        {
            try
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    // 将位图保存为JPEG格式到内存流
                    bitmap.Save(stream, ImageFormat.Jpeg);
                    byte[] data = new byte[stream.Length];
                    // 重置流指针到开始位置
                    stream.Seek(0, SeekOrigin.Begin);
                    // 读取全部字节数据
                    stream.Read(data, 0, Convert.ToInt32(stream.Length));
                    return data;
                }
            }
            catch (Exception ex) { }
            return null;
        }

        /// <summary>
        /// BitmapSource转byte[]数组工具方法
        /// 将WPF的BitmapSource转换为JPEG格式的字节数组
        /// </summary>
        /// <param name="source">待转换的BitmapSource对象</param>
        /// <returns>JPEG格式的字节数组，转换失败时返回null</returns>
        /// <remarks>
        /// 此方法用于处理WPF图像对象
        /// 实现要点：
        /// - 使用JpegBitmapEncoder进行编码
        /// - 设置最高质量(100)保证图像精度
        /// - 包含完整的异常处理和日志记录
        /// </remarks>
        public byte[] BitmapSource2Byte(BitmapSource source)
        {
            try
            {
                // 创建JPEG编码器，设置最高质量
                JpegBitmapEncoder encoder = new JpegBitmapEncoder();
                encoder.QualityLevel = 100;

                using (MemoryStream stream = new MemoryStream())
                {
                    // 添加图像帧并保存到内存流
                    encoder.Frames.Add(BitmapFrame.Create(source));
                    encoder.Save(stream);
                    // 获取字节数组
                    byte[] bit = stream.ToArray();
                    stream.Close();
                    return bit;
                }
            }
            catch (Exception ex)
            {
                // 记录详细的转换异常
                ClassLoger.Error("BitmapSource2Byte", ex);
            }
            return null;
        }

        /// <summary>
        /// 人脸检测核心方法
        /// 实现完整的人脸检测流程：
        /// 1. 图像预处理和尺寸调整
        /// 2. 调用百度AI人脸检测API
        /// 3. 解析检测结果
        /// 4. 分析人脸质量并生成提示信息
        /// </summary>
        /// <param name="image">待检测的图像对象，应为Bitmap类型</param>
        /// <remarks>
        /// 此方法是实时人脸检测的核心
        /// 实现要点：
        /// - 图像尺寸调整以减少内存消耗和计算量
        /// - 多线程处理避免UI阻塞
        /// - 详细的人脸质量分析和提示生成
        /// - 使用Invoke确保UI更新在主线程执行
        /// </remarks>
        public void Detect(object image)
        {
            // 检查图像对象有效性
            if (image != null && image is Bitmap)
            {
                Bitmap img = null;
                try
                {
                    // 获取图像对象
                    img = (Bitmap)image;

                    // 创建较小的副本以减少内存占用
                    Bitmap smallerImg = null;
                    try
                    {
                        // 图像尺寸调整逻辑，限制最大尺寸为400x300
                        int maxWidth = 400;
                        int maxHeight = 300;

                        // 计算新尺寸，保持宽高比
                        double ratioX = (double)maxWidth / img.Width;
                        double ratioY = (double)maxHeight / img.Height;
                        double ratio = Math.Min(ratioX, ratioY);

                        if (ratio < 1.0) // 仅在图像大于目标尺寸时调整
                        {
                            int newWidth = (int)(img.Width * ratio);
                            int newHeight = (int)(img.Height * ratio);

                            // 创建缩小的图像
                            smallerImg = new Bitmap(newWidth, newHeight);
                            using (Graphics g = Graphics.FromImage(smallerImg))
                            {
                                g.DrawImage(img, 0, 0, newWidth, newHeight);
                            }
                        }
                        else
                        {
                            // 图像已足够小，直接克隆
                            smallerImg = new Bitmap(img);
                        }

                        var imgByte = Bitmap2Byte(smallerImg);

                        // 转换为Base64格式
                        string image1 = ConvertImageToBase64(smallerImg);
                        string imageType = "BASE64";

                        if (imgByte != null)
                        {
                            /* 
                             * 百度人脸检测API参数配置
                             * max_face_num：最多检测2张人脸
                             * face_field：返回年龄、质量和颜值信息
                             * 这些参数可根据实际需求调整
                             */
                            var options = new Dictionary<string, object>{
                                {"max_face_num", 2},  // 最多检测2张人脸
                                {"face_field", "age,qualities,beauty"}  // 返回年龄、质量和颜值信息
                            };

                            // 调用百度AI人脸检测API
                            var result = client.Detect(image1, imageType, options);

                            // 将JSON结果反序列化为自定义对象（需定义对应的实体类）
                            FaceDetectInfo detect = JsonHelper.DeserializeObject<FaceDetectInfo>(result.ToString());

                            // 处理检测结果，使用Invoke确保在UI线程更新UI
                            this.Invoke((MethodInvoker)delegate {
                                if (detect != null && detect.result_num > 0)
                                {
                                    // 显示检测到的人脸年龄
                                    ageText.Text = detect.result[0].age.TryToString();
                                    // 保存人脸位置信息，用于绘制人脸框
                                    this.location = detect.result[0].location;

                                    // 初始化质量分析结果字符串
                                    StringBuilder sb = new StringBuilder();

                                    /* 
                                     * 人脸质量详细分析
                                     * 基于百度AI返回的qualities参数
                                     * 对各项质量指标进行判断并生成提示信息
                                     */
                                    if (detect.result[0].qualities != null)
                                    {
                                        // 模糊度检测（值越大越模糊）
                                        if (detect.result[0].qualities.blur >= 0.7)
                                        {
                                            sb.AppendLine("人脸过于模糊");
                                        }
                                        // 完整度检测（值越大越不完整）
                                        if (detect.result[0].qualities.completeness >= 0.4)
                                        {
                                            sb.AppendLine("人脸不完整");
                                        }
                                        // 光照条件检测（值越小光照越差）
                                        if (detect.result[0].qualities.illumination <= 40)
                                        {
                                            sb.AppendLine("灯光光线质量不好");
                                        }

                                        // 面部遮挡情况检测
                                        if (detect.result[0].qualities.occlusion != null)
                                        {
                                            // 左脸颊遮挡检测
                                            if (detect.result[0].qualities.occlusion.left_cheek >= 0.8)
                                            {
                                                sb.AppendLine("左脸颊不清晰");
                                            }
                                            // 左眼遮挡检测
                                            if (detect.result[0].qualities.occlusion.left_eye >= 0.6)
                                            {
                                                sb.AppendLine("左眼不清晰");
                                            }
                                            // 嘴巴遮挡检测
                                            if (detect.result[0].qualities.occlusion.mouth >= 0.7)
                                            {
                                                sb.AppendLine("嘴巴不清晰");
                                            }
                                            // 鼻子遮挡检测
                                            if (detect.result[0].qualities.occlusion.nose >= 0.7)
                                            {
                                                sb.AppendLine("鼻子不清晰");
                                            }
                                            // 右脸颊遮挡检测
                                            if (detect.result[0].qualities.occlusion.right_cheek >= 0.8)
                                            {
                                                sb.AppendLine("右脸颊不清晰");
                                            }
                                            // 右眼遮挡检测
                                            if (detect.result[0].qualities.occlusion.right_eye >= 0.6)
                                            {
                                                sb.AppendLine("右眼不清晰");
                                            }
                                            // 下巴遮挡检测
                                            if (detect.result[0].qualities.occlusion.chin >= 0.6)
                                            {
                                                sb.AppendLine("下巴不清晰");
                                            }

                                            /* 
                                             * 人脸姿态分析
                                             * 分析人脸的三维角度（俯仰角、横滚角、偏航角）
                                             * 给出调整建议
                                             */
                                            if (detect.result[0].pitch >= 20)
                                            {
                                                sb.AppendLine("俯视角度太大");
                                            }
                                            if (detect.result[0].roll >= 20)
                                            {
                                                sb.AppendLine("脸部应该放正");
                                            }
                                            if (detect.result[0].yaw >= 20)
                                            {
                                                sb.AppendLine("脸部应该放正点");
                                            }
                                        }
                                    }

                                    // 人脸尺寸检测
                                    if (detect.result[0].location.height <= 100 || detect.result[0].location.width <= 100)
                                    {
                                        sb.AppendLine("人脸部分过小");
                                    }

                                    // 显示质量分析结果
                                    textBox4.Text = sb.ToString();
                                    // 如果没有质量问题，显示"OK"
                                    if (textBox4.Text.IsNull())
                                    {
                                        textBox4.Text = "OK";
                                    }
                                }
                            });
                        }
                    }
                    finally
                    {
                        // 释放缩小后的图像资源
                        if (smallerImg != null)
                        {
                            smallerImg.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 记录详细的检测异常
                    ClassLoger.Error("Form1.Detect", ex);
                }
                finally
                {
                    // 释放原始图像资源（仅在创建了新Bitmap时释放）
                    if (img != null && img != image)
                    {
                        img.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// 窗体关闭事件处理
        /// 执行资源释放和清理操作：
        /// 1. 取消所有异步操作
        /// 2. 停止视频流
        /// 3. 退出应用程序
        /// </summary>
        /// <param name="sender">事件触发源对象</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// 此方法确保应用程序退出时的资源正确释放
        /// 实现要点：
        /// - 使用取消令牌停止异步任务
        /// - 等待一段时间确保任务完成清理
        /// - 调用Environment.Exit确保进程完全退出
        /// </remarks>
        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            // 请求取消所有后台操作
            _cancellationTokenSource?.Cancel();

            // 停止视频流并释放设备资源
            if (videoSource != null && videoSource.IsRunning)
            {
                videoSource.Stop();
            }

            // 等待100ms让后台任务有时间清理
            Thread.Sleep(100);

            // 退出应用程序，确保所有资源被释放
            System.Environment.Exit(0);
        }

        /// <summary>
        /// 人脸注册按钮事件处理
        /// 实现人脸注册到百度AI平台的流程：
        /// 1. 获取用户信息和分组ID
        /// 2. 捕获当前视频帧
        /// 3. 调用百度AI人脸注册API
        /// 4. 处理注册结果
        /// </summary>
        /// <param name="sender">事件触发源对象</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// 人脸注册是人脸识别系统的基础功能
        /// 实现要点：
        /// - 使用REPLACE动作类型，支持更新已注册人脸
        /// - 包含完整的参数校验和异常处理
        /// - 注册成功后给出明确提示
        /// </remarks>
        private void button7_Click(object sender, EventArgs e)
        {
            /* 
             * 人脸注册核心参数
             * uid：用户唯一标识
             * userInfo：用户附加信息（如姓名、部门等）
             * groupId：用户组ID，用于分组管理
             */
            string uid = "1";  // 用户ID（实际应用中应动态生成或获取）
            string userInfo = textBox6.Text.Trim();  // 用户资料
            string groupId = textBox5.Text.Trim();  // 用户组ID

            // 检查是否有可用的视频设备
            if (comboBox1.Items.Count <= 0)
            {
                MessageBox.Show("请插入视频设备");
                return;
            }
            try
            {
                // 检查视频是否在运行
                if (videoSourcePlayer1.IsRunning)
                {
                    // 获取当前视频帧并转换为BitmapSource
                    BitmapSource bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                    videoSourcePlayer1.GetCurrentVideoFrame().GetHbitmap(),
                                    IntPtr.Zero,
                                    Int32Rect.Empty,
                                    BitmapSizeOptions.FromEmptyOptions());

                    // 转换为字节数组
                    var img = BitmapSource2Byte(bitmapSource);

                    /* 
                     * 人脸注册API参数配置
                     * action_type：指定操作为替换(REPLACE)
                     * REPLACE模式下，如用户已存在则更新人脸信息
                     */
                    var options = new Dictionary<string, object>{
                        {"action_type", "REPLACE"}  // 替换模式，注意参数值为大写
                    };

                    // 调用百度AI人脸注册API
                    var result = client.UserAdd(Convert.ToBase64String(img), "BASE64", groupId, uid, options);

                    // 处理注册结果
                    if (result.Value<int>("error_code") == 0)
                    {
                        MessageBox.Show("注册成功");
                    }
                    else
                    {
                        MessageBox.Show("注册失败:" + result.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                // 显示摄像头相关异常
                MessageBox.Show("摄像头异常：" + ex.Message);
            }
        }

        /// <summary>
        /// 人脸登录按钮事件处理
        /// 实现人脸搜索和身份识别流程：
        /// 1. 获取用户组ID
        /// 2. 捕获当前视频帧
        /// 3. 调用百度AI人脸搜索API
        /// 4. 处理搜索结果，识别用户身份
        /// </summary>
        /// <param name="sender">事件触发源对象</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// 人脸登录是人脸识别的核心应用场景
        /// 实现要点：
        /// - 支持设置匹配阈值和质量控制
        /// - 处理搜索结果并识别最佳匹配用户
        /// - 包含成功和失败场景的处理
        /// </remarks>
        private void button8_Click(object sender, EventArgs e)
        {
            string groupId = textBox5.Text.Trim();  // 使用与注册相同的用户组ID

            // 检查组ID是否为空
            if (string.IsNullOrEmpty(groupId))
            {
                MessageBox.Show("请输入用户组ID", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 检查是否有可用的视频设备
            if (comboBox1.Items.Count <= 0)
            {
                MessageBox.Show("请插入视频设备");
                return;
            }

            try
            {
                // 检查视频是否在运行
                if (videoSourcePlayer1.IsRunning)
                {
                    // 获取当前视频帧并转换为BitmapSource
                    BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                                    videoSourcePlayer1.GetCurrentVideoFrame().GetHbitmap(),
                                    IntPtr.Zero,
                                    Int32Rect.Empty,
                                    BitmapSizeOptions.FromEmptyOptions());

                    // 转换为字节数组
                    var img = BitmapSource2Byte(bitmapSource);

                    // 人脸搜索API参数配置
                    var options = new Dictionary<string, object>{
                        {"match_threshold", 70},  // 匹配阈值设为70
                        {"quality_control", "NORMAL"},  // 中等质量控制
                        {"liveness_control", "LOW"},  // 低级别活体检测
                        {"max_user_num", 3}  // 最多返回3个匹配用户
                    };

                    // 转换为Base64编码
                    var image = Convert.ToBase64String(img);
                    var imageType = "BASE64";

                    // 调用百度AI人脸搜索API
                    var result = client.Search(image, imageType, groupId, options);

                    // 处理搜索结果
                    if (result.Value<int>("error_code") == 0)
                    {
                        // 检查是否有匹配的用户
                        if (result["result"] != null &&
                            result["result"]["user_list"] != null &&
                            result["result"]["user_list"].Count() > 0)
                        {
                            // 获取匹配的用户列表
                            JArray array = result["result"].Value<JArray>("user_list");
                            // 显示匹配到的第一个用户ID
                            textBox7.Text = array[0].Value<string>("user_id");
                            // 显示匹配的相似度分数
                            double score = array[0].Value<double>("score");

                            // 播放登录成功提示音
                            axWindowsMediaPlayer1.URL = "20230522_160638_1.mp3";
                            axWindowsMediaPlayer1.Ctlcontrols.play();

                            // 显示登录成功提示，包含用户ID和相似度
                            MessageBox.Show($"登录成功！用户ID: {textBox7.Text}, 相似度: {score}",
                                "登录成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            // 未找到匹配用户的场景处理
                            MessageBox.Show("未找到匹配的用户，请先注册或检查用户组ID是否正确",
                                "未找到用户", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    else
                    {
                        // 显示登录失败信息
                        MessageBox.Show($"登录失败: {result["error_msg"]}",
                            "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                // 显示摄像头相关异常
                MessageBox.Show("摄像头异常：" + ex.Message);
            }
        }

        /// <summary>
        /// 停止按钮事件处理
        /// 停止音频播放和视频采集
        /// </summary>
        /// <param name="sender">事件触发源对象</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// 此功能用于停止当前操作
        /// 实现要点：
        /// - 停止音频播放
        /// - 停止视频流
        /// - 注意：未释放设备资源，完整实现应包含资源释放
        /// </remarks>
        private void button9_Click(object sender, EventArgs e)
        {
            // 停止音频播放
            axWindowsMediaPlayer1.Ctlcontrols.stop();

            // 检查视频设备是否可用
            if (videoDevices == null || videoDevices.Count == 0)
            {
                return;
            }

            // 停止视频采集
            videoSource.Stop();
            videoSourcePlayer1.Stop();

            // 注释掉的资源释放代码（实际应用中应取消注释）
            //videoSourcePlayer1.Dispose();

            // 注释掉的排序算法提示（与当前功能无关）
            //排序算法
        }

        private void ageText_TextChanged(object sender, EventArgs e)
        {

        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {

        }
    }
}