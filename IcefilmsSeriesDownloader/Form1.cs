/*
 * This essentially works!
 * 
 * Things to add:
 *  x Ability to easily deselect entire seasons
 *  - Ability to retry an episode a certian number of times in case of timeouts
 *  - Change timeout?
 *  x Make the grabber threaded
 *  x Add a status bar with "Grabbing 1x02..." and progress bar
 *  - Tidy up the UI a bit
 *  - Search Icefilms for a show from within this app...?
 *  
 * */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms;
using System.Threading;

namespace IcefilmsSeriesDownloader
{
    public partial class Form1 : Form
    {
        private Random rand;
        private CookieContainer cookieContainer;
        private delegate void ProgressDelegate(int progress, int total);
        private ProgressDelegate progressDelegate;
        private delegate void ListViewDelegate(ListViewItem l);
        private ListViewDelegate listViewDelegate;
        private delegate void StatusDelegate(string status);
        private StatusDelegate statusDelegate;
        private string standardIceFilmsUrl = "https://icefilms.unblocked.uno";
        private bool useStandardReferrer = true;
        private bool useUSERAGENT = true;
        private string standardUSERAGENT =
"Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/54.0.2840.99 Safari/537.36";

        public Form1()
        {
            rand = new Random();
            cookieContainer = new CookieContainer();
            progressDelegate = new ProgressDelegate(updateProgress);
            listViewDelegate = new ListViewDelegate(updateListView);
            statusDelegate = new StatusDelegate(updateStatus);
            InitializeComponent();

        }

        private void updateStatus(string status)
        {
            txtStatus.Text = status;

        }

        private void updateListView(ListViewItem l)
        {
            lvEpisodes.Items.Add(l);
            lvEpisodes.View = View.Details;
            lvEpisodes.Refresh();
        }

        private void updateProgress(int progress, int total)
        {
            prgToolbar.Maximum = total;
            prgToolbar.Value = progress;
        }

        private void btnGo_Click(object sender, EventArgs e)
        {
            lvEpisodes.Items.Clear();
            Thread th = new Thread(new ParameterizedThreadStart(grabData));
            th.IsBackground = true;
            th.Start(edtShowURL.Text);
        }

        private void grabData(object urlo)
        {
            int doneEpisodes = 0, totalEpisodes = 0;
            string url = urlo.ToString();
            bool megaUpOnly = false; //TODO: Make this a setting on the form.
            //textOutput.Text = "";
            this.Invoke(statusDelegate, new object[] { "Grabbing IceFilms page..." });
            String showPage = httpGet(url, "");
            if (showPage == "")
            {
                //textOutput.Text = "Error: Couldn't grab page. Icefilms down?";
                this.Invoke(progressDelegate, new object[] { (int)0, (int)100 });
                this.Invoke(statusDelegate, new object[] { "Error: Couldn't grab page. Is icefilms down?" });
                return;
            }

            // Find the number of episodes
            MatchCollection eMatches = Regex.Matches(showPage, "<a href=(\\\")?/ip.php");
            totalEpisodes = eMatches.Count;
            this.Invoke(progressDelegate, new object[] { doneEpisodes, totalEpisodes });
            this.Invoke(statusDelegate, new object[] { "Found " + totalEpisodes + " episodes." });
            //<a name=[0-9]+></a>(?<title>Season (?<season>[0-9]+) )?[\(]?(?<year>[0-9]{4})[\\)]?.*?</h3>(?<episodes>.*?)(<h3>|</span>)
            MatchCollection matches = Regex.Matches(showPage, "<a name=[0-9]+></a>(Season (?<season>[0-9]+) )?[\\(]?(?<year>[0-9\\?]{4})[\\)]?.*?</h3>(?<episodes>.*?)(?:<h3>|</span>)");
            if (matches.Count < 1)
            {
                //textOutput.Text = "Error: No seasons found.";
                //MessageBox.Show("Error: No seasons found.");
                this.Invoke(progressDelegate, new object[] { (int)0, (int)100 });
                this.Invoke(statusDelegate, new object[] { "Error: No seasons found." });
                return;
            }
            foreach (Match match in matches)
            {
                //textOutput.Text += "Season: " + match.Groups["season"] + " -- Year: " + match.Groups["year"] ;
                MatchCollection episodeMatches = Regex.Matches(match.Groups["episodes"].ToString(), "ip.php\\?v=(?<vid>[0-9]+)&>(?<season>[0-9]+)x(?<episode>[0-9]+) (?<title>.*?)</a>");
                if (episodeMatches.Count < 1)
                {
                    //textOutput.Text = "Error: No episodes found for season #" + match.Groups["season"];
                    //MessageBox.Show("Error: No episodes found for season #" + match.Groups["season"] + ".");
                    this.Invoke(progressDelegate, new object[] { (int)0, (int)100 });
                    this.Invoke(statusDelegate, new object[] { "Error: No episodes found for season #" + match.Groups["season"] + "." });
                    return;
                }
                foreach (Match episode in episodeMatches)
                {
                    String sourcesPage;
                    String sec;
                    getSecretSourcesPage(episode, out sourcesPage, out sec);
                    if (sec == "")
                    {
                        // Oh well... let's just skip this episode.
                        //textOutput.Text += "Error: Couldn't find sec " + episode.Groups["season"] + "x" + episode.Groups["episode"] ;
                        Console.WriteLine("Error: Couldn't find sec " + episode.Groups["season"] + "x" + episode.Groups["episode"]);
                        continue;
                    }

                    //textOutput.Text += "  Season " + episode.Groups["season"] + ", Episode " + episode.Groups["episode"] + ": " + WebUtility.HtmlDecode(episode.Groups["title"].ToString()) + " -- ID: " + episode.Groups["vid"] ;                    

                    MatchCollection sourceMatches = Regex.Matches(sourcesPage, "go\\(([0-9]+)\\)'>Source #[0-9]+:.*?</a>");
                    bool breakOut = false;
                    bool retrylogic = false;
                    
                    
                    for (int i = (sourceMatches.Count - 1); i >= 0 && !breakOut; i--)
                    {
                        var filehost = sourceMatches[i].Groups[0].ToString();
                        filehost = filehost.Substring(filehost.IndexOf("<span"));
                        filehost = getCleanHtml(filehost);
                        if (filehost.ToLower() == "fileweed" && sourceMatches.Count > 1) continue;
                        int retries = 0;
                        String sourceResponse = getSourceDetails(episode, sec, sourceMatches, i);
                        if (sourceResponse == "" && retrylogic)
                        {
                            tryagain:
                            Console.Write("Error: Retrying source #" + i + " for " + episode.Groups["season"] + "x" + episode.Groups["episode"] + ".");
                            String tsourcesPage;
                            //textOutput.Text += "Error: Couldn't grab source for " + episode.Groups["season"] + "x" + episode.Groups["episode"] + "." ;
                            getSecretSourcesPage(episode, out tsourcesPage, out sec);
                            sourceResponse = getSourceDetails(episode, sec, sourceMatches, i);
                            retries++;
                            if (sourceResponse != "") { Console.Write("Success!" + Environment.NewLine); }
                            else
                            {
                                Console.Write("\t Re-Retrying source #" + i + " for " + episode.Groups["season"] + "x" + episode.Groups["episode"] + ".");
                                getSecretSourcesPage(episode, out tsourcesPage, out sec);
                                sourceResponse = getSourceDetails(episode, sec, sourceMatches, i);
                                retries++;
                                if (sourceResponse != "") { Console.Write("Success!" + Environment.NewLine); }
                                else
                                {
                                    Console.Write("Failed: Source #" + i + " S" + episode.Groups["season"] + "E" + episode.Groups["episode"] + Environment.NewLine);
                                    continue;
                                }
                            }
                            if (retries < 5) goto tryagain;
                        }
                        //if (IsNumeric(sourceResponse))
                        if (!sourceResponse.Contains("GMorBMlet"))
                        {
                            //textOutput.Text += "Error: "+sourceResponse+" -- Couldn't grab source for " + episode.Groups["season"] + "x" + episode.Groups["episode"] + "." ;
                            continue;
                        }
                        String[] urlSplit = Regex.Split(sourceResponse, "GMorBMlet\\.php\\?url=");
                        String sourceURL = urlSplit[1];
                        //textOutput.Text += episode.Groups["season"] + "x" + episode.Groups["episode"] + " -- " + Uri.UnescapeDataString(sourceURL) ;
                        String[] cols = { episode.Groups["season"].ToString(), episode.Groups["episode"].ToString(), WebUtility.HtmlDecode(episode.Groups["title"].ToString()), Uri.UnescapeDataString(sourceURL) };
                        ListViewItem lvEpisode = new ListViewItem(cols);
                        lvEpisode.Checked = true;
                        //lvEpisodes.Items.Add(lvEpisode);
                        lvEpisodes.Invoke(listViewDelegate, new object[] { lvEpisode });
                        break;
                    }
                    doneEpisodes++;
                    this.Invoke(progressDelegate, new object[] { doneEpisodes, totalEpisodes });
                }
            }
            this.Invoke(progressDelegate, new object[] { (int)100, (int)100 });
            this.Invoke(statusDelegate, new object[] { "Done!" });
        }

        private string getSourceDetails(Match episode, String sec, MatchCollection sourceMatches, int i)
        {
            this.Invoke(statusDelegate, new object[] { "Trying source #" + i + " for " + episode.Groups["season"] + "x" + episode.Groups["episode"] + "..." });
            //if (megaUpOnly && sourceMatches[i].Groups[0].ToString() != "//img593.imageshack.us/img593/8770/megauplogo.png")
            //{
            //    //textOutput.Text += "Error: Not megaupload " + episode.Groups["season"] + "x" + episode.Groups["episode"] ;
            //    continue; // If we only want megaupload, and this isn't megaupload, try the next source
            //}
            // Craft our POST data
            String postData = "id=" + sourceMatches[i].Groups[1] + "&s=" + (10000+rand.Next(2, 18)) + "&iqs=&url=&m=" + (10000 +rand.Next(80, 200) ) + "&cap= &sec=" + sec + "&t=" + episode.Groups["vid"];

            String sourceResponse = httpPost("https://icefilms.unblocked.uno/membersonly/components/com_iceplayer/video.phpAjaxResp.php?s="+ sourceMatches[i].Groups[1]+ "&t=" + episode.Groups["vid"], postData, "https://icefilms.unblocked.uno/membersonly/components/com_iceplayer/video.php?h=374&w=631&vid=" + episode.Groups["vid"] + "&img=");
            //String sourceResponse = httpPost("https://ipv6.icefilms.info/membersonly/components/com_iceplayer/video.phpAjaxResp.php?s="+ sourceMatches[i].Groups[1]+ "&t=" + episode.Groups["vid"], postData, "https://ipv6.icefilms.info/membersonly/components/com_iceplayer/video.php?h=374&w=631&vid=" + episode.Groups["vid"] + "&img=");
            //if (sourceResponse=="") throw new System.Exception();
            return sourceResponse;
        }

        private void getSecretSourcesPage(Match episode, out String sourcesPage, out String sec)
        {

            this.Invoke(statusDelegate, new object[] { "Grabbing sources for " + episode.Groups["season"] + "x" + episode.Groups["episode"] + "..." });
            sourcesPage = httpGet("https://icefilms.unblocked.uno/membersonly/components/com_iceplayer/video.php?h=374&w=631&vid=" + episode.Groups["vid"] + "&img=", "", "https://icefilms.unblocked.uno/ip.php?v=" + episode.Groups["vid"]);
            //            sourcesPage = httpGet("https://ipv6.icefilms.info/membersonly/components/com_iceplayer/video.php?h=374&w=631&vid=" + episode.Groups["vid"] + "&img=", "", "https://ipv6.icefilms.info/ip.php?v=" + episode.Groups["vid"]);
            // TODO: video.php might (should) give us a cookie. We need to remember it!
            // Need to extract sec. This is the same for any source.
            Match secMatch = Regex.Match(sourcesPage, "f\\.lastChild\\.value=\"(?<sec>.*?)\"");
            sec = secMatch.Groups["sec"].ToString();

        }

        private String httpPost(string strPage, string strBuffer)
        {
            return httpPost(strPage, strBuffer, useStandardReferrer ? standardIceFilmsUrl : "");
        }

        private String httpPost(string strPage, string strBuffer, string referer)
        {
            try
            {

                //Our postvars
                byte[] buffer = Encoding.ASCII.GetBytes(strBuffer);
                //Initialization
                System.Net.ServicePointManager.Expect100Continue = false;
                HttpWebRequest WebReq = (HttpWebRequest)WebRequest.Create(strPage);
                //Our method is post, otherwise the buffer (postvars) would be useless
                WebReq.Method = "POST";
                //Timeout
                WebReq.Timeout = 10000; // 10 second timeout. Should be long enough.
                //We use form contentType, for the postvars.
                WebReq.ContentType = "application/x-www-form-urlencoded";
                //The length of the buffer (postvars) is used as contentlength.
                WebReq.ContentLength = buffer.Length;
                WebReq.Referer = referer;
                WebReq.Headers["Origin"] = standardIceFilmsUrl;//"https://ipv6.icefilms.info";
                WebReq.Accept = "*/*";
                WebReq.Headers.Add("Accept-Language", "en-US,en;q=0.8");
               // WebReq.Headers.Add("Upgrade-Insecure-Requests", "1");
                WebReq.UserAgent = useUSERAGENT ? standardUSERAGENT : "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/54.0.2840.99 Safari/537.36";
                WebReq.CookieContainer = cookieContainer;
                Console.WriteLine(WebReq.RequestUri);
                //We open a stream for writing the postvars
                Stream PostData = WebReq.GetRequestStream();
                //Now we write, and afterwards, we close. Closing is always important!
                
                PostData.Write(buffer, 0, buffer.Length);
                PostData.Close();
                //Get the response handle, we have no true response yet!
                HttpWebResponse WebResp = (HttpWebResponse)WebReq.GetResponse();
                //Let's show some information about the response
                //Console.WriteLine(WebResp.StatusCode);
                //Console.WriteLine(WebResp.Server);

                //Now, we read the response (the string), and output it.
                Stream Answer = WebResp.GetResponseStream();
                StreamReader _Answer = new StreamReader(Answer);
                return _Answer.ReadToEnd();
            }
            catch (Exception e)
            {
                Console.WriteLine(((WebException)e.InnerException).Status);
                Console.WriteLine(((WebException)e.InnerException).Response);
                throw;
            }
        }

        private String httpGet(string strPage, string strVars)
        {
            return httpGet(strPage, strVars, useStandardReferrer ? standardIceFilmsUrl : "");
        }

        private String httpGet(string strPage, string strVars, string referer)
        {
            //Initialization
            HttpWebRequest WebReq = (HttpWebRequest)WebRequest.Create(string.Format("{0}{1}", strPage, strVars));
            //This time, our method is GET.
            WebReq.Method = "GET";
            WebReq.Referer = referer;
            WebReq.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
            WebReq.Headers.Add("Accept-Language", "en-US,en;q=0.8");
            WebReq.Headers.Add("Upgrade-Insecure-Requests","1");
            WebReq.UserAgent = useUSERAGENT ? standardUSERAGENT: "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/54.0.2840.99 Safari/537.36";
            // Timeout
            WebReq.Timeout = 10000; // 10 second timeout. Should be long enough.
            WebReq.CookieContainer = cookieContainer;
            //From here on, it's all the same as above.
            HttpWebResponse WebResp = (HttpWebResponse)WebReq.GetResponse();
            //Let's show some information about the response
            //Console.WriteLine(WebResp.StatusCode);
            //Console.WriteLine(WebResp.Server);

            //Now, we read the response (the string), and output it.
            Stream Answer = WebResp.GetResponseStream();
            StreamReader _Answer = new StreamReader(Answer);
            return _Answer.ReadToEnd();
        }

        public static System.Boolean IsNumeric(System.Object Expression)
        {
            if (Expression == null || Expression is DateTime)
                return false;

            if (Expression is Int16 || Expression is Int32 || Expression is Int64 || Expression is Decimal || Expression is Single || Expression is Double || Expression is Boolean)
                return true;

            try
            {
                if (Expression is string)
                    Double.Parse(Expression as string);
                else
                    Double.Parse(Expression.ToString());
                return true;
            }
            catch { } // just dismiss errors but return false
            return false;
        }

        private void btnCopy_Click(object sender, EventArgs e)
        {
            String urls = "";
            foreach (ListViewItem episode in lvEpisodes.Items)
            {
                if (episode.Checked)
                {
                    urls += " "+ episode.SubItems[episode.SubItems.Count - 1].Text;
                }
            }
            Clipboard.SetText(urls);
            MessageBox.Show("Selected URLs Copied to Clipboard, donation page loading after this...");
         
        {
            System.Diagnostics.Process.Start("http://www.icedivx.com/donate.php");
        }
    }

        private void button1_Click(object sender, EventArgs e)
        {
            for (int i = 1; i < 21; i++)
            {
                ListViewItem t = new ListViewItem(new string[] { "1", i.ToString(), "Episode title " + i, "http://www.megaupload.com/?d=1" + i });
                t.Checked = true;
                lvEpisodes.Items.Add(t);
            }
            for (int i = 1; i < 21; i++)
            {
                ListViewItem t = new ListViewItem(new string[] { "2", i.ToString(), "Episode title " + i, "http://www.megaupload.com/?d=2" + i });
                t.Checked = true;
                lvEpisodes.Items.Add(t);
            }
        }

        private void testToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem lvItem in lvEpisodes.Items)
            {
                if (!lvItem.Checked && lvItem.SubItems[0].Text == lvEpisodes.SelectedItems[0].SubItems[0].Text)
                {
                    lvItem.Checked = true;
                }
            }
        }

        private void uncheckEntireSeasonToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem lvItem in lvEpisodes.Items)
            {
                try
                {
                    if (lvItem.Checked && lvItem.SubItems[0].Text == lvEpisodes.SelectedItems[0].SubItems[0].Text)
                    {
                        lvItem.Checked = false;
                    }
                }
                catch { }
            }
        }

        private void selectAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem lvItem in lvEpisodes.Items)
            {
                lvItem.Checked = true;
            }
        }

        private void deselectAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem lvItem in lvEpisodes.Items)
            {
                lvItem.Checked = false;
            }
        }

        private void quitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutBox1 about = new AboutBox1();
            about.ShowDialog(this);
        }

        private void clearListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            lvEpisodes.Items.Clear();
        }

        private void forALLRESULTSToolStripMenuItem_Click(object sender, EventArgs e)
        {
            frmSearch f = new frmSearch(0);
            f.Show();
        }

        private void forMovieToolStripMenuItem_Click(object sender, EventArgs e)
        {
            frmSearch f = new frmSearch(2);
            f.Show();
        }

        private void forTVShowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            frmSearch f = new frmSearch(1);
            f.Show();
        }

        private void edtShowURL_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (this.edtShowURL.Text.Count(x => x == '/') >= 3)
            {
                try
                {
                    var firstpart = this.edtShowURL.Text.Substring(0, this.edtShowURL.Text.IndexOf('/', 6) - 1);
                    standardIceFilmsUrl = firstpart ?? standardIceFilmsUrl;
                }
                catch { }
            }
            if (e.KeyChar == '\r')
            {

                btnGo_Click(sender, e);
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.edtShowURL.Text = standardIceFilmsUrl + "/tv/series/6/5468";            //edtShowURL.Text
        }
        public string getCleanHtml(string html)
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);
            // return HtmlAgilityPack.HtmlEntity.DeEntitize(doc.DocumentNode.InnerText); // Use if you want to convert HTML entities to their literal view
            return doc.DocumentNode.InnerText; // if you want to keep HTML entities
        }
    }
}
