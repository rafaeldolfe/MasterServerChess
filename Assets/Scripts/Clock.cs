/*
MIT License

Copyright (c) 2019 Radek Lžičař

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System.Collections;
using UnityEngine;
using UnityEngine.Assertions;

namespace Chessticle
{
    public class Clock
    {
        ChessColor m_CurrentPlayer = ChessColor.None;
        bool m_Running = true;
        double whiteTime;
        double blackTime;
        public Clock(int timeSeconds)
        {
            whiteTime = timeSeconds;
            blackTime = timeSeconds;
            Task.Get(RunClock());
        }

        private IEnumerator RunClock()
        {
            while (true)
            {
                if (m_CurrentPlayer == ChessColor.White)
                {
                    whiteTime -= Time.deltaTime;
                    if (whiteTime < 0)
                    {
                        whiteTime = 0d;
                    }
                }
                else
                {
                    blackTime -= Time.deltaTime;
                    if (blackTime < 0)
                    {
                        blackTime = 0d;
                    }
                }
                yield return new WaitForEndOfFrame();
            }
        }

        public void SetTime(double whitePlayerServerTime, double blackPlayerServerTime)
        {
            whiteTime = whitePlayerServerTime;
            blackTime = blackPlayerServerTime;
            
            if (whiteTime < 0)
            {
                whiteTime = 0d;
            }

            if (blackTime < 0)
            {
                blackTime = 0d;
            }
        }
        public void SwitchPlayer()
        {
            if (!m_Running)
            {
                return;
            }

            switch (m_CurrentPlayer)
            {
                case ChessColor.None:
                    m_CurrentPlayer = ChessColor.Black;
                    break;
                case ChessColor.White:
                    m_CurrentPlayer = ChessColor.Black;
                    break;
                case ChessColor.Black:
                    m_CurrentPlayer = ChessColor.White;
                    break;
            }
        }

        public double GetTime(ChessColor color)
        {
            return color == ChessColor.White ? whiteTime : blackTime;
        }
        // public float GetTime(ChessColor chessColor, double serverTime)
        // {
        //     Assert.IsTrue(chessColor != ChessColor.None);
        //
        //     float delta = 0;
        //     if (m_Running && m_CurrentPlayer == chessColor)
        //     {
        //         delta = (float) (serverTime - m_LastSwitchServerTime);
        //     }
        //
        //     var time = chessColor == ChessColor.White ? m_WhiteTime : m_BlackTime;
        //     var value = Mathf.Max(0, (float) (time - delta));
        //     return value;
        // }

        public void Stop()
        {
            m_Running = false;
        }
    }
}