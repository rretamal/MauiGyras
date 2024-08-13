using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiGyras.Game
{
    public class RecognizedWord
    {
        public string Text { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Opacity { get; set; }
        public float Scale { get; set; }

        public RecognizedWord(string text, float x, float y)
        {
            Text = text;
            X = x;
            Y = y;
            Opacity = 1.0f;
            Scale = 1.0f;
        }

        public void Update()
        {
            Opacity -= 0.01f; 
            Scale += 0.005f; 
            Y -= 0.5f;
        }
    }
}
