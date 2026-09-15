using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

interface IObjectPoolable
{
    void OnGet();
    void OnRelease();
    void Release();
}
