# 透视校正拟合：上屏/下屏 UI 横线拉平、机台竖边拉直、圆屏拉成正圆。线段坐标取自源视频 Hough 检测（544x960 源像素）。
# 在 video/ 目录下运行: python fit_rectify.py  -> rectify_H.npy（make_demo.py 用）
import numpy as np, cv2
from scipy.optimize import least_squares
Hs=[((112,41),(434,20)),((108,171),(201,166)),((322,170),(489,161)),((190,206),(522,189)),
    ((290,518),(510,515)),((190,532),(283,531)),((212,668),(329,667)),((402,529),(497,529)),((357,622),(458,622))]
Vs=[((108,183),(111,43)),((481,19),(486,161)),((70,163),(74,31)),((520,0),(527,142))]
D=np.load('rectify_disc_pts.npy').astype(float)
C=np.array([272.,480.])
def mk(p):
    l1,l2,th,k,a=p
    T=np.array([[1,0,-C[0]],[0,1,-C[1]],[0,0,1]])
    P=np.array([[1,0,0],[0,1,0],[l1*1e-4,l2*1e-4,1]])
    A=np.array([[1,k,0],[0,a,0],[0,0,1]])
    R=np.array([[np.cos(th),-np.sin(th),0],[np.sin(th),np.cos(th),0],[0,0,1]])
    return np.linalg.inv(T)@R@A@P@T
def tf(H,pts):
    pts=np.asarray(pts,float); q=np.c_[pts,np.ones(len(pts))]@H.T; return q[:,:2]/q[:,2:]
def ang(H,a,b,vert):
    q=tf(H,[a,b]); d=q[1]-q[0]
    if vert:
        if d[1]<0: d=-d
        return np.degrees(np.arctan2(d[0],d[1]))
    return np.degrees(np.arctan2(d[1],abs(d[0])))*np.sign(d[0])
def kasa(q):
    A=np.c_[2*q,np.ones(len(q))]; b=(q**2).sum(1); sol=np.linalg.lstsq(A,b,rcond=None)[0]; return sol[:2]
def res(p):
    H=mk(p); r=[ang(H,a,b,0)/0.3 for a,b in Hs]+[ang(H,a,b,1)/0.3 for a,b in Vs]
    q=tf(H,D); c=kasa(q); rr=np.hypot(*(q-c).T); r+=list((rr-rr.mean())/rr.mean()/0.01/np.sqrt(len(D)/10))
    return np.array(r)
s=least_squares(res,[0,0,0,0,1],bounds=([-5,-5,-0.2,-0.3,0.8],[5,5,0.2,0.3,1.2]))
H=mk(s.x); print('params',np.round(s.x,4))
print('H',[round(ang(H,a,b,0),2) for a,b in Hs]); print('V',[round(ang(H,a,b,1),2) for a,b in Vs])
print('raw H',[round(ang(np.eye(3),a,b,0),2) for a,b in Hs]); print('raw V',[round(ang(np.eye(3),a,b,1),2) for a,b in Vs])
q=tf(H,D); c=kasa(q); rr=np.hypot(*(q-c).T); print('disc r %.1f std %.2f'%(rr.mean(),rr.std()))
np.save('rectify_H.npy',H); print(repr(np.round(H,8).tolist()))
im=cv2.imread('work/src_128.png')
w=cv2.warpPerspective(im,H,(544,960),flags=cv2.INTER_CUBIC)
for y in range(0,960,40): cv2.line(w,(0,y),(544,y),(0,255,0),1)
for x in range(0,544,40): cv2.line(w,(x,0),(x,960),(0,255,0),1)
cv2.imwrite('work/warp_grid.png',w)
