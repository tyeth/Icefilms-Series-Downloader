using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace IcefilmsSeriesDownloader
{
    public partial class frmSearch : Form
    {
        public frmSearch()
        {
            InitializeComponent();
        }

        public frmSearch(int i)
        {
            //deal with type of search
            InitializeComponent();
        }

        private void txtSearch_DragDrop(object sender, DragEventArgs e)
        {
            DropText(e);
            e.Effect = DragDropEffects.None;
        }

        private void DropText(DragEventArgs e)
        {
            if (e.GetType() == typeof(string))
            {
                txtSearch.Text = e.Data.GetData(System.Windows.Forms.DataFormats.Text).ToString();
            }
            else
            {
                try
                {
                    txtSearch.Text = e.Data.GetData(System.Windows.Forms.DataFormats.Text).ToString();
                }
                catch (Exception)
                {

                }
            }
        }

        private void frmSearch_DragDrop(object sender, DragEventArgs e)
        {
           // DropText(e);
        }

        private void txtSearch_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Link;
            if (e.KeyState==1){

            }else{
                Console.WriteLine( e.KeyState);// == MouseButtons.None)
            }
            
                this.Activate();
            // DropText(e);
        }

        private void frmSearch_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Link;
        }

        private void frmSearch_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Link;
        }

        private void txtSearch_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Link;
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            
        }
    }
}
